using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>One point of the "timestamp in the recording" <-> "engine RPM" map.</summary>
[System.Serializable]
public class RpmKey
{
    [Tooltip("Position in the recording, in seconds.")]
    public float time;
    [Tooltip("Engine RPM at that moment of the recording.")]
    public float rpm = 1000f;
}

/// <summary>One engine recording and how it should follow the RPM.</summary>
[System.Serializable]
public class EngineRecording
{
    public string label = "Recording";
    public AudioClip clip;
    [Range(0f, 2f)] public float volume = 1f;

    [Tooltip("OFF (Loop): the clip is looped and pitch-shifted to follow the RPM. Best for short steady loops.\n" +
             "ON (Scrub): the playback position follows the RPM using the keys below. Best for one long rev-up (or rev-down) recording.")]
    public bool scrub = false;

    [Tooltip("Loop mode only: the RPM this clip was recorded at. At this RPM it plays at its natural pitch and is the loudest.")]
    public float recordedRpm = 3000f;

    [Tooltip("Scrub mode only: timestamps in the recording mapped to RPM. Add at least 2 keys, e.g. (0 s = 900 rpm), (4 s = 3000 rpm), (9 s = 7500 rpm).")]
    public RpmKey[] keys = new RpmKey[0];

    [Tooltip("Scrub mode only: length in seconds of the small piece that is looped around the spot that matches the current RPM.\n" +
             "Example: keys (1 s = 2000 rpm) and (3 s = 4000 rpm) with Hold Length 0.5 -> at 2000 rpm only about 0.75 s - 1.25 s is played (looped), at 4000 rpm only about 2.75 s - 3.25 s.\n" +
             "Smaller = stays closer to the exact moment you keyed, but loops more often. 0 = off (old behaviour: the playhead runs on until the RPM drifts too far).")]
    [Min(0f)] public float holdLength = 0f;
}

/// <summary>
/// AUTO SLICE: one recording + the RPM range it covers (from -> to).
/// The script reads the clip length, cuts the clip into equal slices,
/// turns every slice into a short loop and gives each loop its own part of the RPM range.
/// Example: 3 s clip, 1000-2500 rpm, slice 1 s, chunk 0.2 s ->
///   slice 1 (0-1 s) -> 0.2 s loop for 1000-1500 rpm
///   slice 2 (1-2 s) -> 0.2 s loop for 1500-2000 rpm
///   slice 3 (2-3 s) -> 0.2 s loop for 2000-2500 rpm
/// </summary>
[System.Serializable]
public class AutoSliceRecording
{
    public string label = "Slice set";
    public AudioClip clip;
    [Range(0f, 2f)] public float volume = 1f;

    [Tooltip("RPM at the START of the recording.")]
    public float rpmFrom = 1000f;
    [Tooltip("RPM at the END of the recording.")]
    public float rpmTo = 2500f;

    [Tooltip("Wanted length of one slice in seconds. Clip length / this = number of slices (rounded down, min 1).")]
    [Min(0.1f)] public float sliceSeconds = 1f;
    [Tooltip("Length in seconds of the loop cut out of each slice.")]
    [Min(0.05f)] public float chunkSeconds = 0.2f;
    [Tooltip("Where in the slice the loop is cut. 0 = start of slice, 0.5 = middle, 1 = end of slice.")]
    [Range(0f, 1f)] public float chunkPosition = 0f;
    [Tooltip("Length in seconds of the blend at the loop seam. Removes the click when the loop restarts.")]
    [Range(0.005f, 0.1f)] public float seamFade = 0.03f;
}

/// <summary>
/// SOUND ONLY. Reads RPM, gear, throttle and clutch from CarTelemetry and turns them into engine sound.
/// The engine sound follows the real RPM: recordings are mapped to RPM values, crossfaded,
/// and pitch-corrected so the pitch always matches the current RPM.
/// It never touches how the car drives. Put it on the car root, next to CarTelemetry.
/// </summary>
public class EngineAudio : MonoBehaviour
{
    // ------------------------------------------------------------------ Inspector

    [Header("Car")]
    [Tooltip("Leave empty to find it automatically on this object or its parents.")]
    [SerializeField] private CarTelemetry telemetry;

    [Header("One-shot sounds")]
    public AudioClip startClip;
    public AudioClip shiftClip;
    [Tooltip("Plays once when you let go of the gas at high RPM.")]
    public AudioClip decelerationClip;
    public bool startEngineOnPlay = true;
    [Tooltip("How much of the Start clip plays before the engine fades in (0-1).")]
    [Range(0f, 1f)] public float startOverlap = 0.7f;
    [Range(0f, 1f)] public float shiftVolume = 0.8f;
    [Range(0f, 1f)] public float decelerationVolume = 0.8f;
    [Tooltip("Also play the Shift sound when the car downshifts.")]
    public bool playShiftOnDownshift = false;
    [Tooltip("The Deceleration one-shot only plays if RPM (0-1 between Min and Max RPM) was above this when you lifted off the gas.")]
    [Range(0f, 1f)] public float decelerationMinRpm = 0.5f;

    [Header("AUTO SLICE (easiest): low / mid / high files")]
    [Tooltip("Put your low, mid and high clips here. Set the RPM range of each.\n" +
             "The script reads how long each clip is, cuts it into slices, makes a short loop of each slice and maps the loops to the RPM range.\n" +
             "If any clip is here, it replaces Quick setup and Accel Recordings.\n" +
             "IMPORTANT: each AudioClip must have Load Type = Decompress On Load (so the script can read the samples).")]
    public AutoSliceRecording[] autoSlices;

    [Header("Quick setup: 4 looping recordings")]
    [Tooltip("Used when the 'Accel Recordings' list below is empty. Each clip is looped, crossfaded by RPM and pitch-shifted around the RPM you enter.")]
    public AudioClip idleClip;
    public float idleClipRpm = 900f;
    public AudioClip lowRpmClip;
    public float lowClipRpm = 2700f;
    public AudioClip midRpmClip;
    public float midClipRpm = 4800f;
    public AudioClip highRpmClip;
    public float highClipRpm = 7000f;

    [Header("Advanced: RPM recordings (optional)")]
    [Tooltip("ON-throttle recordings (engine under load). If any clip is assigned here, it replaces the Quick setup above.")]
    public EngineRecording[] accelRecordings;
    [Tooltip("OFF-throttle recordings (engine braking / lifting off the gas). Optional. If empty, the on-throttle sound just gets quieter off the gas.")]
    public EngineRecording[] decelRecordings;

    [Header("RPM range")]
    [Tooltip("Lowest RPM used by the audio (your idle RPM).")]
    public float minRpm = 900f;
    [Tooltip("ON: take the max RPM from CarTelemetry.")]
    public bool useTelemetryMaxRpm = true;
    public float maxRpm = 8000f;

    [Header("Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("Only used when there are no Decel Recordings: how loud the engine is off the gas (1 = same as full throttle).")]
    [Range(0f, 1f)] public float offThrottleVolume = 0.6f;
    [Tooltip("Volume of the Decel Recordings.")]
    [Range(0f, 1f)] public float decelSetVolume = 1f;
    [Tooltip("How much the engine gets quieter while the clutch is in during a gear change.")]
    [Range(0f, 1f)] public float clutchDip = 0.4f;

    [Header("Transition smoothness (higher = faster / snappier, lower = smoother)")]
    [Tooltip("How fast the audio RPM follows the car's RPM.")]
    public float rpmSmoothing = 20f;
    [Tooltip("How fast each recording's volume moves to its target. Prevents pops.")]
    public float volumeSmoothing = 20f;
    [Tooltip("How fast the on-throttle / off-throttle blend reacts to the gas pedal.")]
    public float throttleSmoothing = 6f;
    [Tooltip("How wide the crossfade between two neighbouring recordings is. 1 = blend over the whole gap, lower = cleaner single-recording zones.")]
    [Range(0.1f, 1f)] public float crossfadeWidth = 0.8f;

    [Header("Pitch")]
    [Tooltip("1 = pitch follows RPM exactly (RPM / recorded RPM). 0 = no pitch change at all. Around 0.75 is a good start.")]
    [Range(0f, 1f)] public float pitchFollow = 0.75f;
    public float minPitch = 0.6f;
    public float maxPitch = 1.8f;

    [Header("Scrub mode (recordings with timestamp keys)")]
    [Tooltip("How far (as a fraction of the RPM) the playhead may drift from the wanted RPM before it jumps to a better spot.")]
    [Range(0.03f, 0.4f)] public float scrubWindow = 0.12f;
    [Tooltip("Length in seconds of the crossfade when the playhead jumps. Longer = smoother but more blurred.")]
    [Range(0.03f, 0.5f)] public float scrubCrossfade = 0.12f;

    [Header("Audio playback settings")]
    [Tooltip("0 = plain 2D sound (always audible, good for the player car). 1 = fully 3D (use for AI cars).")]
    [Range(0f, 1f)] public float spatialBlend = 0f;
    [Tooltip("Lower number = more important. Unity drops high numbers first when there are too many sounds.")]
    [Range(0, 256)] public int priority = 100;
    [Tooltip("Optional: send the engine to an Audio Mixer group.")]
    public AudioMixerGroup outputMixerGroup;

    [Header("Debug")]
    public bool showDebug = false;

    // ------------------------------------------------------------------ Runtime

    private class Voice
    {
        public EngineRecording rec;
        public AudioSource[] src;          // 1 source (loop) or 2 sources (scrub)
        public RpmKey[] keys;              // sorted by time, only for scrub
        public float center;               // RPM where this recording is the loudest
        public float weight;               // 0..1 position-based weight
        public float targetVol;
        public float vol;                  // smoothed volume
        public int active;                 // scrub: which source is the current one
        public bool started;               // scrub: has a segment been started?
        public float[] gain = new float[2];// scrub: crossfade gains
        public float segEnd;               // scrub + hold: playhead time at which the loop jumps back
        public float segRpm;               // scrub + hold: the RPM the current piece was picked for
    }

    private Voice[] accelVoices = new Voice[0];
    private Voice[] decelVoices = new Voice[0];
    private AudioSource oneShotSrc;
    private readonly List<AudioClip> madeClips = new List<AudioClip>(); // clips the script cut itself

    private float audioRpm;
    private float onWeight;
    private float engineFade;
    private bool engineOn;
    private bool starting;
    private bool wasLifting = true;
    private int lastGear = 1;
    private float shiftTimer;
    private float decelTimer;

    // ------------------------------------------------------------------ Setup

    private void Awake()
    {
        if (telemetry == null) telemetry = GetComponentInParent<CarTelemetry>();
        if (telemetry == null)
        {
            Debug.LogError("[EngineAudio] No CarTelemetry found. Put this script on the car root or drag CarTelemetry into the slot.", this);
            enabled = false;
            return;
        }

        oneShotSrc = MakeSource("Engine_OneShots", false);
        oneShotSrc.volume = 1f; // PlayOneShot is multiplied by this, so it must not be 0

        EngineRecording[] onSet;
        if (HasAutoClip(autoSlices)) onSet = BuildAutoSlices(autoSlices);
        else if (HasClip(accelRecordings)) onSet = accelRecordings;
        else onSet = QuickSet();

        accelVoices = BuildSet("On", onSet);
        decelVoices = BuildSet("Off", decelRecordings);

        if (accelVoices.Length == 0)
            Debug.LogWarning("[EngineAudio] No engine recordings assigned (Auto Slice, Quick setup clips or Accel Recordings).", this);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < madeClips.Count; i++)
            if (madeClips[i] != null) Destroy(madeClips[i]);
        madeClips.Clear();
    }

    private void Start()
    {
        lastGear = telemetry.Gear;
        audioRpm = Mathf.Max(minRpm, telemetry.Rpm);
        onWeight = telemetry.Throttle;
        if (startEngineOnPlay) StartEngine();
    }

    private EngineRecording[] QuickSet()
    {
        return new[]
        {
            Quick("Idle", idleClip, idleClipRpm),
            Quick("Low", lowRpmClip, lowClipRpm),
            Quick("Mid", midRpmClip, midClipRpm),
            Quick("High", highRpmClip, highClipRpm)
        };
    }

    private static EngineRecording Quick(string label, AudioClip clip, float rpm)
    {
        return new EngineRecording { label = label, clip = clip, recordedRpm = rpm };
    }

    private static bool HasClip(EngineRecording[] recs)
    {
        if (recs == null) return false;
        for (int i = 0; i < recs.Length; i++)
            if (recs[i] != null && recs[i].clip != null) return true;
        return false;
    }

    private static bool HasAutoClip(AutoSliceRecording[] recs)
    {
        if (recs == null) return false;
        for (int i = 0; i < recs.Length; i++)
            if (recs[i] != null && recs[i].clip != null) return true;
        return false;
    }

    // ------------------------------------------------------------------ Auto slice

    // Reads each clip length, cuts it in slices, makes one short loop per slice,
    // and gives each loop its own RPM band. Result is a normal loop list for BuildSet.
    private EngineRecording[] BuildAutoSlices(AutoSliceRecording[] sets)
    {
        var result = new List<EngineRecording>();

        for (int i = 0; i < sets.Length; i++)
        {
            AutoSliceRecording a = sets[i];
            if (a == null || a.clip == null) continue;

            AudioClip src = a.clip;
            int channels = src.channels;
            int freq = src.frequency;
            int totalFrames = src.samples;

            var data = new float[totalFrames * channels];
            if (!src.GetData(data, 0))
            {
                Debug.LogWarning("[EngineAudio] Cannot read '" + src.name + "'. Set its Load Type to 'Decompress On Load' in the import settings.", this);
                continue;
            }

            float length = (float)totalFrames / freq;
            int sliceCount = Mathf.Max(1, Mathf.FloorToInt(length / Mathf.Max(0.1f, a.sliceSeconds)));
            float sliceLen = length / sliceCount;                       // real slice length, uses the whole clip
            float chunkLen = Mathf.Min(a.chunkSeconds, sliceLen);

            int chunkFrames = Mathf.Max(64, Mathf.RoundToInt(chunkLen * freq));
            int fadeFrames = Mathf.Clamp(Mathf.RoundToInt(a.seamFade * freq), 8, chunkFrames / 2);

            float rpmStep = (a.rpmTo - a.rpmFrom) / sliceCount;

            for (int s = 0; s < sliceCount; s++)
            {
                float sliceStart = s * sliceLen;
                float start = sliceStart + (sliceLen - chunkLen) * a.chunkPosition;
                int startFrame = Mathf.RoundToInt(start * freq);

                // Need chunkFrames + fadeFrames of audio. Slide the start back if the clip is too short at the end.
                int needed = chunkFrames + fadeFrames;
                startFrame = Mathf.Clamp(startFrame, 0, Mathf.Max(0, totalFrames - needed));
                int availableFade = Mathf.Min(fadeFrames, Mathf.Max(0, totalFrames - startFrame - chunkFrames));

                var chunk = new float[chunkFrames * channels];
                for (int f = 0; f < chunkFrames; f++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int srcIdx = (startFrame + f) * channels + c;
                        float sample = data[Mathf.Min(srcIdx, data.Length - 1)];

                        // Seam: blend the audio that comes AFTER the loop into its start (equal power),
                        // so the loop end flows straight into the loop start.
                        if (f < availableFade)
                        {
                            float t = (float)f / availableFade * 0.5f * Mathf.PI;
                            float after = data[(startFrame + chunkFrames + f) * channels + c];
                            sample = sample * Mathf.Sin(t) + after * Mathf.Cos(t);
                        }
                        chunk[f * channels + c] = sample;
                    }
                }

                string name = src.name + "_slice" + s;
                AudioClip made = AudioClip.Create(name, chunkFrames, channels, freq, false);
                made.SetData(chunk, 0);
                madeClips.Add(made);

                float bandFrom = a.rpmFrom + rpmStep * s;
                float bandTo = bandFrom + rpmStep;
                float center = (bandFrom + bandTo) * 0.5f;

                result.Add(new EngineRecording
                {
                    label = (string.IsNullOrEmpty(a.label) ? src.name : a.label) + "_" + s,
                    clip = made,
                    volume = a.volume,
                    scrub = false,
                    recordedRpm = center
                });

                if (showDebug)
                    Debug.Log("[EngineAudio] " + name + ": clip " + length.ToString("F2") + " s, slice " + sliceLen.ToString("F2") +
                              " s, loop " + chunkLen.ToString("F2") + " s from " + start.ToString("F2") + " s -> " +
                              bandFrom.ToString("F0") + "-" + bandTo.ToString("F0") + " rpm", this);
            }
        }

        return result.ToArray();
    }

    private Voice[] BuildSet(string setName, EngineRecording[] recs)
    {
        var list = new List<Voice>();

        if (recs != null)
        {
            for (int i = 0; i < recs.Length; i++)
            {
                EngineRecording r = recs[i];
                if (r == null || r.clip == null) continue;

                var v = new Voice { rec = r };
                string baseName = "Engine_" + setName + "_" + (string.IsNullOrEmpty(r.label) ? i.ToString() : r.label);

                bool scrub = r.scrub;
                if (scrub && (r.keys == null || r.keys.Length < 2))
                {
                    Debug.LogWarning("[EngineAudio] '" + r.label + "' is set to Scrub but has fewer than 2 RPM keys, so it is looped instead.", this);
                    scrub = false;
                }

                if (scrub)
                {
                    var sorted = (RpmKey[])r.keys.Clone();
                    System.Array.Sort(sorted, (x, y) => x.time.CompareTo(y.time));
                    v.keys = sorted;

                    float lo = float.MaxValue;
                    float hi = float.MinValue;
                    for (int k = 0; k < sorted.Length; k++)
                    {
                        lo = Mathf.Min(lo, sorted[k].rpm);
                        hi = Mathf.Max(hi, sorted[k].rpm);
                    }
                    v.center = (lo + hi) * 0.5f;
                    v.src = new[] { MakeSource(baseName + "_A", false), MakeSource(baseName + "_B", false) };
                }
                else
                {
                    v.center = r.recordedRpm;
                    v.src = new[] { MakeSource(baseName, true) };
                }

                for (int s = 0; s < v.src.Length; s++) v.src[s].clip = r.clip;
                list.Add(v);
            }
        }

        list.Sort((p, q) => p.center.CompareTo(q.center));
        return list.ToArray();
    }

    private AudioSource MakeSource(string objName, bool loop)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform, false);

        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.volume = 0f;
        s.spatialBlend = spatialBlend;
        s.dopplerLevel = 0f;
        s.priority = priority;
        s.outputAudioMixerGroup = outputMixerGroup;
        return s;
    }

    // ------------------------------------------------------------------ Public API (optional)

    public void StartEngine()
    {
        if (engineOn || starting) return;
        StartCoroutine(StartRoutine());
    }

    public void StopEngine()
    {
        engineOn = false;
    }

    private IEnumerator StartRoutine()
    {
        starting = true;
        if (startClip != null)
        {
            oneShotSrc.PlayOneShot(startClip, masterVolume);
            yield return new WaitForSeconds(startClip.length * startOverlap);
        }
        engineOn = true;
        starting = false;
    }

    // ------------------------------------------------------------------ Update

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float topRpm = useTelemetryMaxRpm ? telemetry.MaxRpm : maxRpm;
        topRpm = Mathf.Max(topRpm, minRpm + 1f);

        float rpmNow = Mathf.Clamp(telemetry.Rpm, minRpm, topRpm);
        audioRpm = Mathf.Lerp(audioRpm, rpmNow, 1f - Mathf.Exp(-rpmSmoothing * dt));

        float throttle = telemetry.Throttle;
        onWeight = Mathf.Lerp(onWeight, throttle, 1f - Mathf.Exp(-throttleSmoothing * dt));

        float rpm01 = Mathf.InverseLerp(minRpm, topRpm, rpmNow);
        int gear = telemetry.Gear;

        shiftTimer -= dt;
        decelTimer -= dt;

        // Shift sound: plays the moment the HUD gear changes
        bool upshift = gear > lastGear && lastGear >= 1;
        bool downshift = gear < lastGear && gear >= 1;
        if (engineOn && shiftTimer <= 0f && (upshift || (downshift && playShiftOnDownshift)))
        {
            PlayOneShot(shiftClip, shiftVolume);
            shiftTimer = 0.5f;
        }
        lastGear = gear;

        // Deceleration one-shot: gas released while revs are high
        bool lifting = throttle < 0.05f;
        if (engineOn && lifting && !wasLifting && rpm01 > decelerationMinRpm && decelTimer <= 0f)
        {
            PlayOneShot(decelerationClip, decelerationVolume);
            decelTimer = 1.5f;
        }
        wasLifting = lifting;

        // Engine fade in/out and clutch dip
        engineFade = Mathf.MoveTowards(engineFade, engineOn ? 1f : 0f, dt * 2f);
        float master = masterVolume * engineFade * Mathf.Lerp(1f, 1f - clutchDip, telemetry.Clutch);

        // On-throttle / off-throttle blend
        float accelMul;
        float decelMul;
        if (decelVoices.Length > 0)
        {
            accelMul = Mathf.Sqrt(onWeight);
            decelMul = Mathf.Sqrt(1f - onWeight) * decelSetVolume;
        }
        else
        {
            accelMul = Mathf.Lerp(offThrottleVolume, 1f, onWeight);
            decelMul = 0f;
        }

        UpdateSet(accelVoices, audioRpm, master * accelMul, dt);
        UpdateSet(decelVoices, audioRpm, master * decelMul, dt);
    }

    private void UpdateSet(Voice[] set, float rpm, float loudness, float dt)
    {
        int n = set.Length;
        if (n == 0) return;

        // 1) Weights: crossfade between the two recordings whose RPM surrounds the current RPM
        for (int i = 0; i < n; i++) set[i].weight = 0f;

        if (n == 1 || rpm <= set[0].center) set[0].weight = 1f;
        else if (rpm >= set[n - 1].center) set[n - 1].weight = 1f;
        else
        {
            for (int i = 0; i < n - 1; i++)
            {
                float a = set[i].center;
                float b = set[i + 1].center;
                if (rpm < a || rpm > b) continue;

                float t = b > a ? (rpm - a) / (b - a) : 1f;
                t = Mathf.Clamp01((t - 0.5f) / Mathf.Max(0.01f, crossfadeWidth) + 0.5f);
                t = t * t * (3f - 2f * t); // smoothstep
                set[i].weight = 1f - t;
                set[i + 1].weight = t;
                break;
            }
        }

        // 2) Drive every recording
        float volK = 1f - Mathf.Exp(-volumeSmoothing * dt);
        for (int i = 0; i < n; i++)
        {
            Voice v = set[i];
            v.targetVol = Mathf.Sqrt(v.weight) * v.rec.volume * loudness; // sqrt = equal-power crossfade
            v.vol = Mathf.Lerp(v.vol, v.targetVol, volK);

            if (v.keys != null) UpdateScrub(v, rpm, dt);
            else UpdateLoop(v, rpm);
        }
    }

    // ------------------------------------------------------------------ Loop mode

    private void UpdateLoop(Voice v, float rpm)
    {
        AudioSource s = v.src[0];

        // Silent and not needed: stop it so it does not use an audio voice
        if (v.vol <= 0.001f && v.targetVol <= 0.001f)
        {
            s.volume = 0f;
            if (s.isPlaying) s.Stop();
            return;
        }

        float ratio = rpm / Mathf.Max(1f, v.rec.recordedRpm);
        s.pitch = Mathf.Clamp(Mathf.Pow(ratio, pitchFollow), minPitch, maxPitch);
        s.volume = v.vol;

        if (!s.isPlaying)
        {
            s.time = Random.value * Mathf.Max(0f, v.rec.clip.length - 0.05f);
            s.Play();
        }
    }

    // ------------------------------------------------------------------ Scrub mode

    private void UpdateScrub(Voice v, float rpm, float dt)
    {
        // Silent and not needed: stop both sources
        if (v.vol <= 0.001f && v.targetVol <= 0.001f)
        {
            for (int i = 0; i < v.src.Length; i++)
            {
                v.src[i].volume = 0f;
                if (v.src[i].isPlaying) v.src[i].Stop();
                v.gain[i] = 0f;
            }
            v.started = false;
            return;
        }

        bool hold = v.rec.holdLength > 0.01f;

        // Is it time to jump to a new spot?
        bool needSeek = !v.started || !v.src[v.active].isPlaying;
        if (!needSeek)
        {
            float t = v.src[v.active].time;
            float clipEnd = v.rec.clip.length - 0.08f;

            if (t >= clipEnd)
            {
                needSeek = true;
            }
            else if (hold)
            {
                // Hold mode: keep looping the small piece until it ends,
                // or until the RPM has moved too far away from the RPM this piece was picked for.
                float pieceDrift = Mathf.Abs(rpm - v.segRpm) / Mathf.Max(1f, rpm);
                if (t >= v.segEnd || pieceDrift > scrubWindow) needSeek = true;
            }
            else
            {
                // Old behaviour: the playhead runs on until its RPM no longer matches.
                float playRpm = RpmAtTime(v.keys, t);
                float drift = Mathf.Abs(playRpm - rpm) / Mathf.Max(1f, rpm);
                if (drift > scrubWindow) needSeek = true;
            }
        }

        if (needSeek) BeginSegment(v, rpm);

        // Crossfade between the two sources, and keep each one's pitch matched to the RPM
        float step = dt / Mathf.Max(0.01f, scrubCrossfade);
        for (int i = 0; i < 2; i++)
        {
            AudioSource s = v.src[i];
            float target = (i == v.active) ? 1f : 0f;
            v.gain[i] = Mathf.MoveTowards(v.gain[i], target, step);

            if (i != v.active && v.gain[i] <= 0.0001f)
            {
                s.volume = 0f;
                if (s.isPlaying) s.Stop();
                continue;
            }

            float playRpm = RpmAtTime(v.keys, s.time);
            float ratio = rpm / Mathf.Max(1f, playRpm);
            s.pitch = Mathf.Clamp(Mathf.Pow(ratio, pitchFollow), minPitch, maxPitch);
            s.volume = v.vol * Mathf.Sqrt(v.gain[i]);
        }
    }

    // Starts the other source at the timestamp that matches the wanted RPM, and fades to it.
    private void BeginSegment(Voice v, float rpm)
    {
        int next = v.started ? 1 - v.active : v.active;
        AudioSource s = v.src[next];
        float clipLen = v.rec.clip.length;
        float t;

        if (v.rec.holdLength > 0.01f)
        {
            // Hold mode: pick a small piece CENTERED on the timestamp that belongs to this RPM.
            // The piece must be longer than two crossfades so the loop jump stays smooth.
            float len = Mathf.Max(v.rec.holdLength, scrubCrossfade * 2f + 0.02f);
            len = Mathf.Min(len, Mathf.Max(0.1f, clipLen - 0.1f));

            float centerTime = TimeAtRpm(v.keys, rpm);
            t = Mathf.Clamp(centerTime - len * 0.5f, 0f, Mathf.Max(0f, clipLen - len - 0.05f));

            // Start the jump one crossfade BEFORE the piece ends, so the fade-out stays inside the piece.
            v.segEnd = t + len - scrubCrossfade;
            v.segRpm = rpm;
        }
        else
        {
            // Old behaviour: start slightly BEFORE the wanted spot so the playhead travels through it
            float t0 = TimeAtRpm(v.keys, rpm);
            float slope = RpmAtTime(v.keys, t0 + 0.1f) - RpmAtTime(v.keys, t0 - 0.1f);
            float dir = slope >= 0f ? 1f : -1f;
            float startRpm = Mathf.Max(1f, rpm * (1f - dir * scrubWindow * 0.8f));
            t = TimeAtRpm(v.keys, startRpm);
            t = Mathf.Clamp(t, 0f, Mathf.Max(0f, clipLen - 0.1f));

            v.segEnd = float.MaxValue;
            v.segRpm = rpm;
        }

        s.Stop();
        s.clip = v.rec.clip;
        s.time = t;
        s.volume = 0f;
        s.Play();

        v.gain[next] = v.started ? 0f : 1f; // first start fades in through the volume smoothing
        v.active = next;
        v.started = true;
    }

    // Timestamp -> RPM (piecewise linear between the keys)
    private static float RpmAtTime(RpmKey[] k, float t)
    {
        if (t <= k[0].time) return k[0].rpm;
        for (int i = 0; i < k.Length - 1; i++)
        {
            if (t <= k[i + 1].time)
            {
                float span = k[i + 1].time - k[i].time;
                float u = span > 0.0001f ? (t - k[i].time) / span : 1f;
                return Mathf.Lerp(k[i].rpm, k[i + 1].rpm, u);
            }
        }
        return k[k.Length - 1].rpm;
    }

    // RPM -> timestamp (first matching segment, in time order)
    private static float TimeAtRpm(RpmKey[] k, float rpm)
    {
        for (int i = 0; i < k.Length - 1; i++)
        {
            float a = k[i].rpm;
            float b = k[i + 1].rpm;
            if (rpm >= Mathf.Min(a, b) && rpm <= Mathf.Max(a, b))
            {
                float u = Mathf.Abs(b - a) > 0.01f ? (rpm - a) / (b - a) : 0f;
                return Mathf.Lerp(k[i].time, k[i + 1].time, u);
            }
        }

        // Outside the mapped range: use the key that is closest in RPM
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < k.Length; i++)
        {
            float d = Mathf.Abs(k[i].rpm - rpm);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return k[best].time;
    }

    // ------------------------------------------------------------------ Helpers

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip != null && oneShotSrc != null) oneShotSrc.PlayOneShot(clip, volume * masterVolume);
    }

    private void OnGUI()
    {
        if (!showDebug || telemetry == null) return;
        GUI.Label(new Rect(10, 10, 700, 25),
            "Engine audio   RPM: " + audioRpm.ToString("F0") +
            "   gas blend: " + onWeight.ToString("F2") +
            "   gear: " + telemetry.GearLabel);
    }
}
