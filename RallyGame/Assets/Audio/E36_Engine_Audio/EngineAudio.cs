using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// One sound layer of the car, for example "Engine" or "Exhaust".
/// Drag ALL the clips of that layer into "On Clips". The RPM of every clip is read from the end of
/// its file name:  Engine_00850 -> 850 rpm,  6N2_E36PG_Exhaust_Acc_0800 -> 800 rpm.
/// </summary>
[System.Serializable]
public class EngineAudioLayer
{
    public string name = "Layer";

    [Tooltip("Mix volume of this whole layer.")]
    [Range(0f, 1f)] public float volume = 1f;

    [Header("On-throttle clips (engine under load)")]
    [Tooltip("Select all your clips in the Project window and drag them onto this field's name.\n" +
             "The RPM is read from the number at the END of each file name (Engine_00850 = 850 rpm).")]
    public AudioClip[] onClips;
    [Tooltip("OPTIONAL. Same order as On Clips. A value above 0 overrides the RPM read from the file name.")]
    public float[] onClipRpm;

    [Header("Off-throttle clips (optional)")]
    [Tooltip("Optional clips for when you are off the gas (engine braking). If empty, the on-throttle clips just get quieter.")]
    public AudioClip[] offClips;
    [Tooltip("OPTIONAL. Same order as Off Clips. A value above 0 overrides the RPM read from the file name.")]
    public float[] offClipRpm;

    [Header("Layer settings")]
    [Tooltip("Only used when there are no Off Clips: how loud this layer is off the gas (1 = same as full throttle).\n" +
             "Exhaust is usually much quieter off the gas than the engine.")]
    [Range(0f, 1f)] public float offThrottleVolume = 0.6f;
    [Tooltip("How strongly the over-rev effect is applied to this layer. 0 = none, 1 = normal, 2 = double.")]
    [Range(0f, 2f)] public float overrevAmount = 1f;
}

/// <summary>
/// SOUND ONLY. Reads RPM, gear, throttle and clutch from CarTelemetry and turns them into engine sound.
///
/// - Every clip is looped and played at its natural pitch at its own RPM.
/// - Between two clips the neighbours are crossfaded (equal power) and pitch-corrected, so the pitch always matches the RPM.
/// - Above the highest clip there is an "over-rev zone": the top clip gets extra pitch, an extra bass layer
///   and (optionally) a little grit, so e.g. 7200 rpm sounds different from the 6500 rpm recording.
///
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

    [Header("Layers (Engine + Exhaust, add more if you like)")]
    public EngineAudioLayer[] layers = new[]
    {
        new EngineAudioLayer { name = "Engine",  volume = 0.8f, offThrottleVolume = 0.7f  },
        new EngineAudioLayer { name = "Exhaust", volume = 0.9f, offThrottleVolume = 0.45f }
    };

    [Header("RPM range")]
    [Tooltip("Lowest RPM used by the audio (your idle RPM).")]
    public float minRpm = 800f;
    [Tooltip("ON: take the max RPM from CarTelemetry.")]
    public bool useTelemetryMaxRpm = true;
    public float maxRpm = 7200f;

    [Header("Over-rev zone (the 'extra' 7200 rpm sound)")]
    [Tooltip("ON: above the highest clip the top clip is modified (pitch / bass / grit) so the top end sounds different.")]
    public bool overrevEnabled = true;
    [Tooltip("RPM where the effect starts. 0 = automatic (starts at the highest clip of each layer, e.g. 6500 for the exhaust).")]
    public float overrevStartRpm = 0f;
    [Tooltip("RPM where the effect is at full strength. Set this to your redline.")]
    public float overrevRpm = 7200f;
    [Tooltip("Extra pitch at full effect, on top of the normal RPM pitch. 1 = none, 1.05 = 5% higher.")]
    [Range(1f, 1.3f)] public float overrevPitch = 1.05f;
    [Tooltip("Volume of the top clip at full effect (1 = unchanged).")]
    [Range(1f, 1.3f)] public float overrevVolume = 1.1f;
    [Tooltip("BASS LAYER: volume of an extra copy of the top clip played lower in pitch and low-passed. 0 = off.")]
    [Range(0f, 1f)] public float overrevBassVolume = 0.35f;
    [Tooltip("Pitch of the bass layer compared to the top clip. 0.5 = one octave lower.")]
    [Range(0.25f, 1f)] public float overrevBassPitch = 0.5f;
    [Tooltip("Low-pass cutoff of the bass layer in Hz. Lower = only deep thump, higher = fuller.")]
    [Range(150f, 2000f)] public float overrevBassCutoff = 600f;
    [Tooltip("Optional grit at full effect. 0 = off. Keep it low (0.05 - 0.25), it gets harsh quickly.\nSet it BEFORE pressing Play.")]
    [Range(0f, 1f)] public float overrevDistortion = 0f;

    [Header("Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("How much the engine gets quieter while the clutch is in during a gear change.")]
    [Range(0f, 1f)] public float clutchDip = 0.4f;

    [Header("Transition smoothness (higher = faster / snappier, lower = smoother)")]
    [Tooltip("How fast the audio RPM follows the car's RPM.")]
    public float rpmSmoothing = 20f;
    [Tooltip("How fast each clip's volume moves to its target. Prevents pops.")]
    public float volumeSmoothing = 20f;
    [Tooltip("How fast the on-throttle / off-throttle blend reacts to the gas pedal.")]
    public float throttleSmoothing = 6f;
    [Tooltip("How wide the crossfade between two neighbouring clips is. 1 = blend over the whole gap (smoothest), lower = cleaner single-clip zones.")]
    [Range(0.1f, 1f)] public float crossfadeWidth = 0.8f;

    [Header("Pitch")]
    [Tooltip("1 = pitch follows RPM exactly (RPM / clip RPM). 0 = no pitch change. With clips this close together, 1 is correct.")]
    [Range(0f, 1f)] public float pitchFollow = 1f;
    public float minPitch = 0.5f;
    public float maxPitch = 2f;

    [Header("Loop quality")]
    [Tooltip("0 = off (use the clips as they are). If you hear a tick every time a clip loops, set this to about 0.03.\n" +
             "The end of each clip is blended into its start. Needs Load Type = Decompress On Load on the clips.")]
    [Range(0f, 0.1f)] public float loopSeamFade = 0f;

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
        public AudioClip clip;
        public AudioSource src;
        public float rpm;          // RPM this clip was recorded at
        public float weight;       // 0..1 crossfade weight
        public float targetVol;
        public float vol;          // smoothed volume
        public float pitch = 1f;   // last pitch that was applied
    }

    private class LayerRuntime
    {
        public EngineAudioLayer cfg;
        public Voice[] on = new Voice[0];
        public Voice[] off = new Voice[0];
        public AudioSource bass;                 // over-rev bass layer
        public AudioDistortionFilter distortion; // over-rev grit (optional)
    }

    private LayerRuntime[] runtime = new LayerRuntime[0];
    private AudioSource oneShotSrc;
    private readonly List<AudioClip> madeClips = new List<AudioClip>(); // clips the script made itself (loop seam)

    private float audioRpm;
    private float onWeight;
    private float engineFade;
    private bool engineOn;
    private bool starting;
    private bool wasLifting = true;
    private int lastGear = 1;
    private float shiftTimer;
    private float decelTimer;
    private float debugOver;

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

        oneShotSrc = MakeSource(transform, "Engine_OneShots", false);
        oneShotSrc.volume = 1f; // PlayOneShot is multiplied by this, so it must not be 0

        var list = new List<LayerRuntime>();
        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                EngineAudioLayer cfg = layers[i];
                if (cfg == null) continue;

                var root = new GameObject("Audio_" + (string.IsNullOrEmpty(cfg.name) ? i.ToString() : cfg.name));
                root.transform.SetParent(transform, false);

                var lr = new LayerRuntime { cfg = cfg };
                lr.on = BuildSet(root.transform, "On", cfg.onClips, cfg.onClipRpm);
                lr.off = BuildSet(root.transform, "Off", cfg.offClips, cfg.offClipRpm);
                if (lr.on.Length > 0) SetupOverrev(lr, root.transform);
                list.Add(lr);
            }
        }
        runtime = list.ToArray();

        bool any = false;
        for (int i = 0; i < runtime.Length; i++)
            if (runtime[i].on.Length > 0) any = true;
        if (!any)
            Debug.LogWarning("[EngineAudio] No engine clips assigned. Put your clips into 'Layers > On Clips'.", this);
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

    // Builds one looping AudioSource per clip, sorted by RPM.
    private Voice[] BuildSet(Transform parent, string setName, AudioClip[] clips, float[] manualRpm)
    {
        var list = new List<Voice>();
        if (clips == null) return list.ToArray();

        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip c = clips[i];
            if (c == null) continue;

            float rpm = (manualRpm != null && i < manualRpm.Length && manualRpm[i] > 0f)
                ? manualRpm[i]
                : ParseRpmFromName(c.name);

            if (rpm <= 0f)
            {
                Debug.LogWarning("[EngineAudio] Could not read an RPM from the name '" + c.name +
                                 "'. End the file name with the RPM (Engine_00850) or type it in the RPM list.", this);
                continue;
            }

            AudioClip playClip = loopSeamFade > 0.001f ? SmoothSeam(c) : c;

            var v = new Voice
            {
                clip = playClip,
                rpm = rpm,
                src = MakeSource(parent, setName + "_" + c.name, true)
            };
            v.src.clip = playClip;
            list.Add(v);

            if (showDebug)
                Debug.Log("[EngineAudio] " + setName + " clip '" + c.name + "' -> " + rpm.ToString("F0") + " rpm", this);
        }

        list.Sort((p, q) => p.rpm.CompareTo(q.rpm));
        return list.ToArray();
    }

    // Over-rev extras live on the TOP clip of the on-throttle set.
    private void SetupOverrev(LayerRuntime lr, Transform parent)
    {
        Voice top = lr.on[lr.on.Length - 1];

        if (overrevBassVolume > 0.001f)
        {
            AudioSource b = MakeSource(parent, "Overrev_Bass", true);
            b.clip = top.clip;
            var lp = b.gameObject.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = overrevBassCutoff;
            lr.bass = b;
        }

        if (overrevDistortion > 0.001f)
        {
            lr.distortion = top.src.gameObject.AddComponent<AudioDistortionFilter>();
            lr.distortion.distortionLevel = 0f;
            lr.distortion.enabled = false;
        }
    }

    private AudioSource MakeSource(Transform parent, string objName, bool loop)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);

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

    // Reads the number at the END of a file name: "Engine_00850" -> 850, "6N2_E36PG_Exhaust_Acc_0800" -> 800.
    private static float ParseRpmFromName(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0f;

        int end = s.Length - 1;
        while (end >= 0 && !char.IsDigit(s[end])) end--;
        if (end < 0) return 0f;

        int start = end;
        while (start > 0 && char.IsDigit(s[start - 1])) start--;

        float rpm;
        string digits = s.Substring(start, end - start + 1);
        return float.TryParse(digits, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out rpm) ? rpm : 0f;
    }

    // Makes a copy of the clip whose end flows straight into its start, so the loop does not tick.
    private AudioClip SmoothSeam(AudioClip src)
    {
        int ch = src.channels;
        int freq = src.frequency;
        int total = src.samples;
        int fade = Mathf.Min(Mathf.RoundToInt(loopSeamFade * freq), total / 4);
        if (fade < 8) return src;

        var data = new float[total * ch];
        if (!src.GetData(data, 0))
        {
            Debug.LogWarning("[EngineAudio] Cannot read '" + src.name +
                             "'. Set its Load Type to 'Decompress On Load' in the import settings. Using it unchanged.", this);
            return src;
        }

        int newLen = total - fade;
        var outData = new float[newLen * ch];
        System.Array.Copy(data, outData, newLen * ch);

        // The first 'fade' frames become a blend of (the tail that would follow the loop end) and (the original start).
        // At frame 0 the output equals the frame right after the loop end, so the wrap-around is continuous.
        for (int f = 0; f < fade; f++)
        {
            float t = (float)f / fade * 0.5f * Mathf.PI;
            float head = Mathf.Sin(t);
            float tail = Mathf.Cos(t);
            for (int c = 0; c < ch; c++)
                outData[f * ch + c] = data[f * ch + c] * head + data[(newLen + f) * ch + c] * tail;
        }

        AudioClip made = AudioClip.Create(src.name + "_loop", newLen, ch, freq, false);
        made.SetData(outData, 0);
        madeClips.Add(made);
        return made;
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
        onWeight = Mathf.Clamp01(Mathf.Lerp(onWeight, throttle, 1f - Mathf.Exp(-throttleSmoothing * dt)));

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

        // Drive every layer
        for (int i = 0; i < runtime.Length; i++)
        {
            LayerRuntime lr = runtime[i];
            float over = ComputeOver(lr);
            if (i == 0) debugOver = over;

            float layerMaster = master * lr.cfg.volume;

            float onMul;
            float offMul;
            if (lr.off.Length > 0)
            {
                onMul = Mathf.Sqrt(onWeight);          // equal-power blend between on and off clips
                offMul = Mathf.Sqrt(1f - onWeight);
            }
            else
            {
                onMul = Mathf.Lerp(lr.cfg.offThrottleVolume, 1f, onWeight);
                offMul = 0f;
            }

            UpdateSet(lr.on, audioRpm, layerMaster * onMul, dt, over);
            UpdateSet(lr.off, audioRpm, layerMaster * offMul, dt, 0f);
            UpdateOverrev(lr, over);
        }
    }

    // 0 below the over-rev zone, grows smoothly to 1 (times the layer's amount) at the over-rev RPM.
    private float ComputeOver(LayerRuntime lr)
    {
        if (!overrevEnabled || lr.on.Length == 0) return 0f;

        float start = Mathf.Max(overrevStartRpm, lr.on[lr.on.Length - 1].rpm);
        if (overrevRpm <= start + 1f) return 0f;

        float u = Mathf.Clamp01((audioRpm - start) / (overrevRpm - start));
        u = u * u * (3f - 2f * u); // smoothstep
        return u * lr.cfg.overrevAmount;
    }

    private void UpdateSet(Voice[] set, float rpm, float loudness, float dt, float over)
    {
        int n = set.Length;
        if (n == 0) return;

        // 1) Weights: crossfade between the two clips whose RPM surrounds the current RPM
        for (int i = 0; i < n; i++) set[i].weight = 0f;

        if (n == 1 || rpm <= set[0].rpm) set[0].weight = 1f;
        else if (rpm >= set[n - 1].rpm) set[n - 1].weight = 1f;
        else
        {
            for (int i = 0; i < n - 1; i++)
            {
                float a = set[i].rpm;
                float b = set[i + 1].rpm;
                if (rpm < a || rpm > b) continue;

                float t = b > a ? (rpm - a) / (b - a) : 1f;
                t = Mathf.Clamp01((t - 0.5f) / Mathf.Max(0.01f, crossfadeWidth) + 0.5f);
                t = t * t * (3f - 2f * t); // smoothstep
                set[i].weight = 1f - t;
                set[i + 1].weight = t;
                break;
            }
        }

        // 2) Drive every clip
        float volK = 1f - Mathf.Exp(-volumeSmoothing * dt);
        for (int i = 0; i < n; i++)
        {
            Voice v = set[i];

            // Only the top clip gets the over-rev changes
            float extraPitch = 1f;
            float extraVol = 1f;
            if (i == n - 1 && over > 0f)
            {
                extraPitch = 1f + (overrevPitch - 1f) * over;
                extraVol = 1f + (overrevVolume - 1f) * over;
            }

            v.targetVol = Mathf.Sqrt(v.weight) * loudness * extraVol; // sqrt = equal-power crossfade
            v.vol = Mathf.Lerp(v.vol, v.targetVol, volK);

            UpdateLoop(v, rpm, extraPitch);
        }
    }

    private void UpdateLoop(Voice v, float rpm, float extraPitch)
    {
        AudioSource s = v.src;

        // Silent and not needed: stop it so it does not use an audio voice
        if (v.vol <= 0.001f && v.targetVol <= 0.001f)
        {
            s.volume = 0f;
            if (s.isPlaying) s.Stop();
            return;
        }

        float ratio = rpm / Mathf.Max(1f, v.rpm);
        v.pitch = Mathf.Clamp(Mathf.Pow(ratio, pitchFollow) * extraPitch, minPitch, maxPitch);
        s.pitch = v.pitch;
        s.volume = Mathf.Clamp01(v.vol);

        if (!s.isPlaying)
        {
            s.time = Random.value * Mathf.Max(0f, v.clip.length - 0.05f);
            s.Play();
        }
    }

    // Bass layer + grit that fade in together with the over-rev amount.
    private void UpdateOverrev(LayerRuntime lr, float over)
    {
        if (lr.on.Length == 0) return;
        Voice top = lr.on[lr.on.Length - 1];

        if (lr.distortion != null)
        {
            float d = Mathf.Clamp01(over * overrevDistortion);
            lr.distortion.enabled = d > 0.001f;
            lr.distortion.distortionLevel = d;
        }

        if (lr.bass != null)
        {
            AudioSource b = lr.bass;
            float bassVol = top.vol * over * overrevBassVolume;

            if (bassVol <= 0.001f)
            {
                b.volume = 0f;
                if (b.isPlaying) b.Stop();
                return;
            }

            b.pitch = Mathf.Clamp(top.pitch * overrevBassPitch, 0.1f, 3f);
            b.volume = Mathf.Clamp01(bassVol);

            if (!b.isPlaying)
            {
                b.time = Random.value * Mathf.Max(0f, top.clip.length - 0.05f);
                b.Play();
            }
        }
    }

    // ------------------------------------------------------------------ Helpers

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip != null && oneShotSrc != null) oneShotSrc.PlayOneShot(clip, volume * masterVolume);
    }

    private void OnGUI()
    {
        if (!showDebug || telemetry == null) return;
        GUI.Label(new Rect(10, 10, 800, 25),
            "Engine audio   RPM: " + audioRpm.ToString("F0") +
            "   gas blend: " + onWeight.ToString("F2") +
            "   over-rev: " + debugOver.ToString("F2") +
            "   gear: " + telemetry.GearLabel);
    }
}