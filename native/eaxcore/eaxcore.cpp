// eaxcore.dll - the voice -> room path of the original engine, taken as it is (same headers, same numbers).
// Mic (mono, 48 kHz) in  ->  stereo "what you hear / what goes to the cable" out.
// Mirrors the voice branch of the audio thread in the original engine app.cpp (default settings:
// card mic tone on, mic level 100, wide off, EQ flat, 3D off, no measured IR).
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

#include "reverb_engine.hpp"
#include "card_core.hpp"
#include "card_presets.hpp"
#include "amount_law.hpp"
#include "send_level.hpp"
#include "subsonic.hpp"
#include "step_back.hpp"
#include "mic_tone.hpp"
#include "wet_peak.hpp"
#include "out_chain.hpp"
#include "bass_boost.hpp"
#include "mic_drive.hpp"
#include "voice_shaper.hpp"
#include "voice_fx.hpp"
#include <memory>

#ifdef _WIN32
#define EAX_API extern "C" __declspec(dllexport)
#else
#define EAX_API extern "C"
#endif

using namespace mix;

static const int FS = 48000;
static const float CASCADE_GAIN = AmountLaw::CASCADE * powf(10.0f, (float)WetPeak::Tune().trimDb / 20.0f);

static eax::CoreRoom loudRoom() { eax::CoreRoom r = HC_CORE; r.decayTime = HC_LOUD_DECAY; return r; }

struct Engine {
    std::atomic<int> preset{HAUNTED_CAVERN_INDEX}, amount{0}, enabled{1};
    // mic volume (%), amplifier, bass boost, voice mask (same chain order as the original engine: gain -> mask -> mic tone -> drive/bass -> room)
    std::atomic<int> micVol{100}, ampOn{1}, ampDb{10}, boostOn{0}, boostDb{0}, maskOn{0}, maskIdx{13}, pitchHz{0}, sizePct{100}, echoOrig{1};
    const float* song = nullptr;     // interleaved stereo song for this block (or null); its echo comes from the room too
    float songGain = 1.0f;
    MicTone micToneO; eqb::BassBoost bassO; Subsonic subsonicV;   // same chain, for the un-masked voice feeding the echo
    MicDrive drive;
    eqb::BassBoost bass;
    voice::VoiceShaper shaper;
    std::unique_ptr<voice::VoiceFx> fx;
    bool shaperWas = false;
    int64_t shaperIdle = 0;

    int curPreset = HAUNTED_CAVERN_INDEX;
    eax::EAXReverb* rv = nullptr;
    eax::EAXReverb* drainRv = nullptr;
    int64_t drainLeft = 0;
    bool drainIsCore = false;

    StepBack voiceBack;
    Subsonic subsonic;
    SendLevel sendLevel;
    MicTone micTone;
    OutChain voiceMon;
    eax::CardCore core, coreLoud;
    bool coreLive = false;
    int64_t coreTail = 0;
    float dryNow = 1.0f;
    std::vector<float> orig, tapV, mono, send, tapIn, keep, wl, wr, dl, dr, kl, kr, coreIn, coreOut, loudIn, loudOut;

    Engine() : core(FS, HC_CORE), coreLoud(FS, loudRoom()) {
        voiceBack.prepare(FS, HC_CORE.decayTime);
        subsonic.prepare(FS);
        sendLevel.prepare(FS);
        micTone.prepare(FS);
        micTone.on = true;
        micToneO.prepare(FS); micToneO.on = true; bassO.prepare(FS); subsonicV.prepare(FS);
        voiceMon.prepare(FS);
        bass.prepare(FS);
        shaper.prepare(FS);
        fx.reset(new voice::VoiceFx());
        fx->prepare(FS);
        rv = new eax::EAXReverb(FS, CARD_PRESETS[curPreset].p);
    }
    ~Engine() { delete rv; delete drainRv; }

    void process(const float* in, float* outL, float* outR, int n) {
        const int amt = std::max(0, std::min(100, amount.load()));
        const int want = std::max(0, std::min(NUM_CARD_PRESETS - 1, preset.load()));
        if (want != curPreset) {
            const bool fromCore = curPreset == HAUNTED_CAVERN_INDEX;
            curPreset = want;
            delete drainRv;
            drainRv = rv;
            drainIsCore = fromCore;
            drainLeft = (int64_t)(rv->decayTime() * FS) + FS / 2;
            rv = new eax::EAXReverb(FS, CARD_PRESETS[curPreset].p);
        }

        const size_t N = (size_t)n;
        for (auto* v : {&orig, &tapV, &mono, &send, &tapIn, &keep, &wl, &wr, &dl, &dr, &kl, &kr, &coreIn, &coreOut, &loudIn, &loudOut}) v->resize(N);

        // total mic gain = mic volume x amplifier (same curve as the original engine's mic level)
        double gTot = std::max(0, micVol.load()) / 100.0;
        if (ampOn.load()) gTot *= std::pow(10.0, std::max(0, std::min(26, ampDb.load())) / 20.0);
        const int fader = (int)std::max(0.0, std::min(1000.0, 100.0 * std::pow(gTot, 1.0 / 1.3010)));
        const float micGain = fader <= 0 ? 0.0f : powf((float)fader / 100.0f, 1.3010f);
        for (int i = 0; i < n; ++i) {
            float v = in[i];
            if (!(v > -4.0f && v < 4.0f)) v = 0.0f;
            mono[i] = v * micGain;
            orig[i] = mono[i];
        }
        bool maskActive = false;

        // voice mask (pitch / size / effect)
        {
            const bool mOn = maskOn.load() != 0;
            const int mi = maskIdx.load();
            const bool masked = mi >= 0 && mi < voice::NUM_MASKS;
            shaper.set(pitchHz.load(), sizePct.load(), masked ? voice::MASKS[mi].ratio / 1000.0 : 1.0);
            shaper.anchor(masked ? voice::MASKS[mi].anchor : voice::Anchor{});
            const bool want = mOn && !shaper.neutral();
            maskActive = mOn && (want || (masked && voice::MASKS[mi].fx != voice::FX_NONE));
            if (want) shaperIdle = 0; else if (shaperWas) shaperIdle += n;
            if (want || (mOn && shaperWas && shaperIdle < (int64_t)FS)) {
                if (!shaperWas) { shaper.reset(); shaperWas = true; }
                for (int o = 0; o < n; o += 4096) shaper.process(mono.data() + o, std::min(4096, n - o));
            } else if (shaperWas) shaperWas = false;
            if (mOn && masked && voice::MASKS[mi].fx != voice::FX_NONE) {
                fx->configure(voice::MASKS[mi].fx, voice::MASKS[mi].a, voice::MASKS[mi].b);
                for (int o = 0; o < n; o += 4096) fx->process(mono.data() + o, std::min(4096, n - o));
            } else fx->configure(voice::FX_NONE, 0.0, 0.0);
        }

        for (int i = 0; i < n; ++i) mono[i] = micTone.process(mono[i]);
        drive.set(fader);
        const float gDrive = drive.gain();
        const double autoBass = eqb::BassBoost::boostFor(micGain > 0.0f ? 20.0 * log10((double)micGain) : 0.0);
        bass.set((boostOn.load() ? (double)std::max(0, std::min(12, boostDb.load())) : 0.0) + autoBass);
        const double bassTotal = (boostOn.load() ? (double)std::max(0, std::min(12, boostDb.load())) : 0.0) + autoBass;
        for (int i = 0; i < n; ++i) mono[i] = bass.run(mono[i] * gDrive);
        // the echo is fed from the normal voice (not the mask) when asked; otherwise from the same voice that is heard
        const bool split = echoOrig.load() != 0 && maskActive;
        if (split) {
            bassO.set(bassTotal);
            for (int i = 0; i < n; ++i) orig[i] = bassO.run(micToneO.process(orig[i]) * gDrive);
        } else std::copy(mono.begin(), mono.end(), orig.begin());

        const bool hc = curPreset == HAUNTED_CAVERN_INDEX;
        const bool effectOff = !enabled.load() || amt <= 0;
        const float wetAmt = AmountLaw::send((float)amt / 100.0f);
        const float gSend = effectOff ? 0.0f : wetAmt;
        if (hc) { rv->setCascade(gSend * CASCADE_GAIN); rv->setCentre(AmountLaw::centre(amt)); }

        for (int i = 0; i < n; ++i) {
            const float vs = orig[i];
            const float sm = song ? (song[2 * i] + song[2 * i + 1]) * 0.5f * songGain : 0.0f;
            tapV[i] = subsonicV.process(vs);
            const float level = sendLevel.process(vs + sm);
            keep[i] = hc ? level : 1.0f;
            tapIn[i] = subsonic.process(vs + sm);
            send[i] = tapIn[i] * keep[i] * gSend;
        }
        rv->process(send.data(), wl.data(), wr.data(), n);

        if (hc) { std::fill(kl.begin(), kl.end(), 0.0f); std::fill(kr.begin(), kr.end(), 0.0f); }
        else { std::copy(wl.begin(), wl.end(), kl.begin()); std::copy(wr.begin(), wr.end(), kr.begin()); }

        if (drainRv) {
            std::fill(send.begin(), send.end(), 0.0f);
            drainRv->process(send.data(), dl.data(), dr.data(), n);
            if (!drainIsCore) for (int i = 0; i < n; ++i) { kl[i] += dl[i]; kr[i] += dr[i]; }
            drainLeft -= n;
            if (drainLeft <= 0) { delete drainRv; drainRv = nullptr; }
        }

        if (hc) coreTail = (int64_t)(HC_CORE.decayTime * FS);
        else if (coreTail > 0) coreTail -= n;
        if (hc || coreTail > 0) {
            if (!coreLive) { core.clear(); coreLoud.clear(); coreLive = true; }
            const float gCore = (hc && !effectOff) ? AmountLaw::core(amt) : 0.0f;
            for (int i = 0; i < n; ++i) { coreIn[i] = tapIn[i] * keep[i] * gCore; loudIn[i] = tapIn[i] * (1.0f - keep[i]) * gCore; }
            core.process(coreIn.data(), coreOut.data(), n);
            coreLoud.process(loudIn.data(), loudOut.data(), n);
            for (int i = 0; i < n; ++i) { coreOut[i] += loudOut[i]; kl[i] += coreOut[i]; kr[i] += coreOut[i]; }
        } else coreLive = false;

        const float cutWant = (!effectOff && hc) ? AmountLaw::dryCutDb(amt) : 0.0f;
        const float dryWant = powf(10.0f, -voiceBack.cutDb(cutWant, tapV.data(), n) / 20.0f);
        for (int i = 0; i < n; ++i) {
            dryNow += 0.002f * (dryWant - dryNow);
            const float dry = mono[i] * dryNow;
            float l = drive.process(dry + kl[i]), r = drive.process(dry + kr[i]);
            voiceMon.process(l, r, true, false);
            outL[i] = l; outR[i] = r;
        }
    }
};

EAX_API void* eax_create() { return new Engine(); }
EAX_API void eax_destroy(void* h) { delete (Engine*)h; }
EAX_API void eax_set(void* h, int preset, int amount, int enabled) {
    Engine* e = (Engine*)h;
    e->preset = preset; e->amount = amount; e->enabled = enabled;
}
EAX_API void eax_process(void* h, const float* mono, float* outL, float* outR, int n) {
    if (n > 0) ((Engine*)h)->process(mono, outL, outR, n);
}
EAX_API void eax_process_song(void* h, const float* mono, const float* song, float songGain, float* outL, float* outR, int n) {
    Engine* e = (Engine*)h;
    e->song = song; e->songGain = songGain;
    if (n > 0) e->process(mono, outL, outR, n);
    e->song = nullptr;
}
EAX_API int eax_rooms() { return NUM_CARD_PRESETS; }
EAX_API const char* eax_room_name(int i) { return (i >= 0 && i < NUM_CARD_PRESETS) ? CARD_PRESETS[i].name : ""; }

EAX_API void eax_set_voice(void* h, int micVol, int ampOn, int ampDb, int boostOn, int boostDb, int maskOn, int maskIdx, int pitchHz, int sizePct, int echoOrig) {
    Engine* e = (Engine*)h;
    e->echoOrig = echoOrig;
    e->micVol = micVol; e->ampOn = ampOn; e->ampDb = ampDb; e->boostOn = boostOn; e->boostDb = boostDb;
    e->maskOn = maskOn; e->maskIdx = maskIdx; e->pitchHz = pitchHz; e->sizePct = sizePct;
}
EAX_API int eax_mask_count() { return voice::NUM_MASKS; }
// list order: the 18 X-Fi masks first, then the the original engine own masks in their own order
EAX_API int eax_mask_at(int pos) {
    if (pos < 0 || pos >= voice::NUM_MASKS) return voice::MASK_NEUTRAL;
    return pos < voice::NUM_XFI_MASKS ? pos : voice::OWN_ORDER[pos - voice::NUM_XFI_MASKS];
}
EAX_API const char* eax_mask_name(int i) {
    static char b[64];
    if (i < 0 || i >= voice::NUM_MASKS) { b[0] = 0; return b; }
    const wchar_t* w = voice::MASKS[i].name;
    int k = 0;
    for (; w[k] && k < 62; ++k) b[k] = (char)w[k];
    b[k] = 0;
    return b;
}
EAX_API int eax_mask_ratio(int i) { return (i >= 0 && i < voice::NUM_MASKS) ? voice::MASKS[i].ratio : 1000; }
EAX_API int eax_mask_size(int i) { return (i >= 0 && i < voice::NUM_MASKS) ? voice::MASKS[i].size : 100; }
