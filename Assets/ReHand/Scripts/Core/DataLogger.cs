using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ReHand.Core
{
    /// <summary>One exercise repetition, as written to the events CSV.</summary>
    public struct RepetitionRecord
    {
        /// <summary>Object or target, e.g. "glass_water", "door_handle".</summary>
        public string Item;
        public int RepIndex;
        /// <summary>World-space target position (where the object had to go / the target to touch).</summary>
        public Vector3 TargetPosition;
        /// <summary>Session time (DataLogger.SessionTime) when the repetition started.</summary>
        public double StartTime;
        /// <summary>Session time when it ended (success, miss or timeout).</summary>
        public double EndTime;
        public bool Success;
        /// <summary>Final distance between effector/object and target, in metres (NaN if not applicable).</summary>
        public float FinalDistance;
        /// <summary>True if hand tracking was lost or low-confidence during the repetition.</summary>
        public bool TrackingIssue;
        public string Details;
    }

    /// <summary>One frame of the trajectory CSV. Positions are in tracking space (teleports don't create jumps).</summary>
    public struct FrameSample
    {
        public Pose Head;
        public EffectorSample Left;
        public EffectorSample Right;
    }

    /// <summary>
    /// Writes the session data to two CSV files on the device:
    ///   Sessions/P{id}_{date}_{time}_events.csv      one row per event / repetition
    ///   Sessions/P{id}_{date}_{time}_trajectory.csv  one row per frame (head + both effectors)
    /// Files live in Application.persistentDataPath; on the Quest that is
    /// /sdcard/Android/data/{package name}/files/Sessions (copy them with `adb pull`).
    /// Numbers use '.' as decimal separator regardless of the device language.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DataLogger : MonoBehaviour
    {
        public static DataLogger Instance { get; private set; }

        [Tooltip("Start a session automatically (participant from SessionContext, 'test' by default). Turn off once SessionManager starts sessions.")]
        [SerializeField] private bool _autoStartSession = true;

        [Tooltip("Flush buffered rows to disk at this interval, so a crash or the headset going to sleep loses at most this much data.")]
        [SerializeField, Min(0.1f)] private float _flushIntervalSeconds = 1f;

        public bool IsSessionActive => _events != null;
        /// <summary>Seconds since the session started (the time base of every CSV row).</summary>
        public double SessionTime => IsSessionActive ? Time.realtimeSinceStartupAsDouble - _sessionStart : 0d;
        public string EventsFilePath { get; private set; }
        public string TrajectoryFilePath { get; private set; }

        private const string EventsHeader =
            "session_time_s,utc,participant,block_mode,active_mode,station,exercise,event,item,rep_index," +
            "target_x,target_y,target_z,rep_start_s,rep_end_s,duration_s,success,final_distance_m,tracking_issue,details";

        private const string TrajectoryHeader =
            "session_time_s,frame,active_mode,station,exercise," +
            "head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw," +
            "l_source,l_valid,l_high_conf,l_x,l_y,l_z," +
            "r_source,r_valid,r_high_conf,r_x,r_y,r_z";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private StreamWriter _events;
        private StreamWriter _trajectory;
        private double _sessionStart;
        private float _lastFlush;
        private readonly StringBuilder _sb = new StringBuilder(256);

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ReHand] A second DataLogger was found and disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        protected virtual void Start()
        {
            if (_autoStartSession && !IsSessionActive) StartSession();
        }

        protected virtual void Update()
        {
            if (IsSessionActive && Time.unscaledTime - _lastFlush >= _flushIntervalSeconds) Flush();
        }

        protected virtual void OnApplicationPause(bool paused)
        {
            // The Quest pauses the app when the headset is taken off; the process may be killed afterwards.
            if (paused) Flush();
        }

        protected virtual void OnApplicationQuit() => EndSession();

        protected virtual void OnDestroy()
        {
            if (Instance != this) return;
            EndSession();
            Instance = null;
        }

        /// <summary>Opens new CSV files for the participant in SessionContext. Ends the previous session first.</summary>
        public void StartSession()
        {
            EndSession();

            string folder = Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", Inv);
            string prefix = Path.Combine(folder, $"P{Sanitize(SessionContext.ParticipantId)}_{stamp}");

            EventsFilePath = prefix + "_events.csv";
            TrajectoryFilePath = prefix + "_trajectory.csv";
            _events = new StreamWriter(EventsFilePath, false, new UTF8Encoding(false), 1 << 14);
            _trajectory = new StreamWriter(TrajectoryFilePath, false, new UTF8Encoding(false), 1 << 16);
            _events.WriteLine(EventsHeader);
            _trajectory.WriteLine(TrajectoryHeader);

            _sessionStart = Time.realtimeSinceStartupAsDouble;
            _lastFlush = Time.unscaledTime;
            LogEvent("session_start", details: $"trained_arm={SessionContext.TrainedArm} device={SystemInfo.deviceModel} app={Application.version}");
            Debug.Log($"[ReHand] Logging session to {prefix}_*.csv");
        }

        /// <summary>Writes the last rows and closes the files.</summary>
        public void EndSession()
        {
            if (!IsSessionActive) return;
            LogEvent("session_end");
            _events.Dispose();
            _trajectory.Dispose();
            _events = null;
            _trajectory = null;
        }

        public void Flush()
        {
            if (!IsSessionActive) return;
            _events.Flush();
            _trajectory.Flush();
            _lastFlush = Time.unscaledTime;
        }

        /// <summary>Logs a session event (mode switch, tracking loss, teleport, pause, SUS answer…).</summary>
        public void LogEvent(string eventName, string item = null, string details = null)
        {
            if (!IsSessionActive) return;
            BeginEventRow(eventName, item);
            // rep_index, target xyz, rep start/end, duration, success, final distance, tracking issue
            _sb.Append(",,,,,,,,,,");
            AppendText(details);
            _events.WriteLine(_sb.ToString());
        }

        /// <summary>Logs one finished repetition.</summary>
        public void LogRepetition(in RepetitionRecord r)
        {
            if (!IsSessionActive) return;
            BeginEventRow("repetition", r.Item);
            _sb.Append(r.RepIndex.ToString(Inv)).Append(',');
            AppendVector(r.TargetPosition).Append(',');
            AppendNumber(r.StartTime).Append(',');
            AppendNumber(r.EndTime).Append(',');
            AppendNumber(r.EndTime - r.StartTime).Append(',');
            _sb.Append(r.Success ? '1' : '0').Append(',');
            if (!float.IsNaN(r.FinalDistance)) AppendNumber(r.FinalDistance);
            _sb.Append(',');
            _sb.Append(r.TrackingIssue ? '1' : '0').Append(',');
            AppendText(r.Details);
            _events.WriteLine(_sb.ToString());
        }

        /// <summary>Logs one trajectory frame (called by TrajectoryRecorder).</summary>
        public void LogFrame(in FrameSample f)
        {
            if (!IsSessionActive) return;
            _sb.Clear();
            AppendNumber(SessionTime).Append(',');
            _sb.Append(Time.frameCount.ToString(Inv)).Append(',');
            _sb.Append(SessionContext.ActiveMode).Append(',');
            AppendText(SessionContext.Station).Append(',');
            AppendText(SessionContext.Exercise).Append(',');
            AppendVector(f.Head.position).Append(',');
            AppendNumber(f.Head.rotation.x).Append(',');
            AppendNumber(f.Head.rotation.y).Append(',');
            AppendNumber(f.Head.rotation.z).Append(',');
            AppendNumber(f.Head.rotation.w).Append(',');
            AppendEffector(f.Left).Append(',');
            AppendEffector(f.Right);
            _trajectory.WriteLine(_sb.ToString());
        }

        private void BeginEventRow(string eventName, string item)
        {
            _sb.Clear();
            AppendNumber(SessionTime).Append(',');
            _sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Inv)).Append(',');
            AppendText(SessionContext.ParticipantId).Append(',');
            _sb.Append(SessionContext.BlockMode).Append(',');
            _sb.Append(SessionContext.ActiveMode).Append(',');
            AppendText(SessionContext.Station).Append(',');
            AppendText(SessionContext.Exercise).Append(',');
            AppendText(eventName).Append(',');
            AppendText(item).Append(',');
        }

        private StringBuilder AppendEffector(in EffectorSample e)
        {
            _sb.Append(e.Source ?? "none").Append(',');
            _sb.Append(e.Valid ? '1' : '0').Append(',');
            _sb.Append(e.HighConfidence ? '1' : '0').Append(',');
            if (e.Valid) AppendVector(e.TrackingPosition);
            else _sb.Append(",,");
            return _sb;
        }

        private StringBuilder AppendVector(Vector3 v)
        {
            AppendNumber(v.x).Append(',');
            AppendNumber(v.y).Append(',');
            return AppendNumber(v.z);
        }

        private StringBuilder AppendNumber(double value) => _sb.Append(value.ToString("0.#####", Inv));

        private StringBuilder AppendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return _sb;
            bool quote = text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!quote) return _sb.Append(text);
            return _sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        }

        private static string Sanitize(string id)
        {
            var sb = new StringBuilder(id.Length);
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }
    }
}
