using System;
using System.Collections;
using ReHand.Core;
using UnityEngine;
using UnityEngine.Events;

namespace ReHand.Exercises
{
    /// <summary>Outcome of one repetition, passed to listeners (UI, feedback, SessionManager).</summary>
    public struct RepetitionResult
    {
        public int RepIndex;
        public bool Success;
        public string Item;
        public float Duration;
        public float FinalDistance;
        public string Reason;
    }

    /// <summary>
    /// Shared repetition cycle for every exercise:
    /// start → (begin rep → success / miss / timeout → short pause) × N → completed.
    ///
    /// Subclasses implement what a repetition looks like (OnRepetitionBegin, OnRepetitionEnd)
    /// and call CompleteRepetition when it succeeds or fails. This class times each repetition,
    /// handles timeouts and pause, flags tracking problems and writes every repetition to the DataLogger.
    /// </summary>
    public abstract class ExerciseBase : MonoBehaviour
    {
        [Header("Exercise")]
        [Tooltip("Name written to the logs, e.g. 'grasp', 'sort', 'reach'.")]
        [SerializeField] private string _exerciseId = "exercise";
        [SerializeField, Min(1)] private int _repetitions = 10;
        [Tooltip("A repetition not finished in this time counts as missed. 0 = no timeout.")]
        [SerializeField, Min(0f)] private float _repetitionTimeoutSeconds = 30f;
        [Tooltip("Pause between two repetitions.")]
        [SerializeField, Min(0f)] private float _delayBetweenRepetitions = 1.5f;

        [Header("Testing")]
        [Tooltip("Start by itself after the delay below. Turn off once SessionManager starts the exercises.")]
        [SerializeField] private bool _autoStart = true;
        [SerializeField, Min(0f)] private float _autoStartDelay = 3f;

        [Header("Optional")]
        [SerializeField] private InputModeManager _inputModeManager;
        [SerializeField] private ExerciseFeedback _feedback;

        [Header("Events")]
        public UnityEvent OnExerciseStarted = new UnityEvent();
        public UnityEvent OnExerciseCompleted = new UnityEvent();

        public event Action<int> RepetitionStarted;
        public event Action<RepetitionResult> RepetitionEnded;
        /// <summary>Completed repetitions / total, 0..1 (for the progress bar).</summary>
        public event Action<float> ProgressChanged;

        public string ExerciseId => _exerciseId;
        public int Repetitions { get => _repetitions; set => _repetitions = Mathf.Max(1, value); }
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRepetitionActive { get; private set; }
        /// <summary>0-based index of the current repetition.</summary>
        public int RepIndex { get; private set; } = -1;
        public int SuccessCount { get; private set; }
        public int MissCount { get; private set; }
        /// <summary>Seconds the current repetition has been running (pauses excluded).</summary>
        public float RepetitionElapsed { get; private set; }

        protected ExerciseFeedback Feedback => _feedback;
        protected DataLogger Logger => DataLogger.Instance;

        private double _repStartSessionTime;
        private int _repStartTrackingIssues;
        private Coroutine _nextRepRoutine;

        protected virtual void Awake()
        {
            if (_inputModeManager == null) _inputModeManager = FindAnyObjectByType<InputModeManager>();
            if (_feedback == null) _feedback = FindAnyObjectByType<ExerciseFeedback>();
        }

        protected virtual IEnumerator Start()
        {
            if (!_autoStart) yield break;
            yield return new WaitForSeconds(_autoStartDelay);
            if (!IsRunning) StartExercise();
        }

        protected virtual void Update()
        {
            if (!IsRunning || IsPaused || !IsRepetitionActive) return;

            RepetitionElapsed += Time.deltaTime;
            if (_repetitionTimeoutSeconds > 0f && RepetitionElapsed >= _repetitionTimeoutSeconds)
            {
                OnRepetitionTimeout();
                return;
            }
            OnRepetitionUpdate();
        }

        // ---------------------------------------------------------------- control

        public void StartExercise()
        {
            if (IsRunning) StopExercise();

            IsRunning = true;
            IsPaused = false;
            RepIndex = -1;
            SuccessCount = 0;
            MissCount = 0;
            SessionContext.SetExercise(_exerciseId);
            Logger?.LogEvent("exercise_start", details: $"repetitions={_repetitions}");
            OnExerciseStart();
            OnExerciseStarted.Invoke();
            ProgressChanged?.Invoke(0f);
            BeginNextRepetition();
        }

        /// <summary>Ends the exercise early (e.g. the therapist skips it).</summary>
        public void StopExercise()
        {
            if (!IsRunning) return;
            if (_nextRepRoutine != null) StopCoroutine(_nextRepRoutine);
            if (IsRepetitionActive) OnRepetitionEnd(false);
            IsRepetitionActive = false;
            IsRunning = false;
            Logger?.LogEvent("exercise_stopped", details: Summary());
            OnExerciseEnd();
            SessionContext.SetExercise("");
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            IsPaused = true;
            Logger?.LogEvent("exercise_paused");
            OnPauseChanged(true);
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;
            IsPaused = false;
            Logger?.LogEvent("exercise_resumed");
            OnPauseChanged(false);
        }

        // ---------------------------------------------------------------- for subclasses

        /// <summary>Called once when the exercise starts, before the first repetition.</summary>
        protected virtual void OnExerciseStart() { }

        /// <summary>Set up repetition <paramref name="repIndex"/> (place the object, enable interaction…).</summary>
        protected abstract void OnRepetitionBegin(int repIndex);

        /// <summary>Called every frame while a repetition is active and not paused.</summary>
        protected virtual void OnRepetitionUpdate() { }

        /// <summary>Clean up after a repetition (disable interaction, return objects…).</summary>
        protected virtual void OnRepetitionEnd(bool success) { }

        /// <summary>Called when the repetition times out. Default: counts it as missed.</summary>
        protected virtual void OnRepetitionTimeout() =>
            CompleteRepetition(false, CurrentItem, CurrentTarget, float.NaN, "timeout");

        /// <summary>Called once after the last repetition (or when stopped).</summary>
        protected virtual void OnExerciseEnd() { }

        protected virtual void OnPauseChanged(bool paused) { }

        /// <summary>Object / target name for the current repetition (logged with timeouts).</summary>
        protected virtual string CurrentItem => _exerciseId;

        /// <summary>Target position for the current repetition (logged with timeouts).</summary>
        protected virtual Vector3 CurrentTarget => Vector3.zero;

        /// <summary>Ends the current repetition, logs it and schedules the next one.</summary>
        protected void CompleteRepetition(bool success, string item, Vector3 target, float finalDistance, string reason)
        {
            if (!IsRunning || !IsRepetitionActive) return;
            IsRepetitionActive = false;

            if (success) SuccessCount++; else MissCount++;

            bool trackingIssue = _inputModeManager != null
                && (_inputModeManager.TrackingIssueCounter != _repStartTrackingIssues || _inputModeManager.IsHandsLostWarning);

            Logger?.LogRepetition(new RepetitionRecord
            {
                Item = item,
                RepIndex = RepIndex,
                TargetPosition = target,
                StartTime = _repStartSessionTime,
                EndTime = Logger.SessionTime,
                Success = success,
                FinalDistance = finalDistance,
                TrackingIssue = trackingIssue,
                Details = reason,
            });

            var result = new RepetitionResult
            {
                RepIndex = RepIndex,
                Success = success,
                Item = item,
                Duration = RepetitionElapsed,
                FinalDistance = finalDistance,
                Reason = reason,
            };

            OnRepetitionEnd(success);
            RepetitionEnded?.Invoke(result);
            ProgressChanged?.Invoke((RepIndex + 1) / (float)_repetitions);

            _nextRepRoutine = StartCoroutine(NextRepetitionAfterDelay());
        }

        /// <summary>Logs an event inside the current repetition (grab, release, drink…).</summary>
        protected void LogRepEvent(string eventName, string item = null, string details = null) =>
            Logger?.LogEvent(eventName, item ?? CurrentItem, details != null ? $"rep={RepIndex} {details}" : $"rep={RepIndex}");

        // ---------------------------------------------------------------- internals

        private IEnumerator NextRepetitionAfterDelay()
        {
            float wait = _delayBetweenRepetitions;
            while (wait > 0f)
            {
                if (!IsPaused) wait -= Time.deltaTime;
                yield return null;
            }
            _nextRepRoutine = null;
            BeginNextRepetition();
        }

        private void BeginNextRepetition()
        {
            if (RepIndex + 1 >= _repetitions)
            {
                FinishExercise();
                return;
            }

            RepIndex++;
            RepetitionElapsed = 0f;
            _repStartSessionTime = Logger != null ? Logger.SessionTime : 0d;
            _repStartTrackingIssues = _inputModeManager != null ? _inputModeManager.TrackingIssueCounter : 0;
            IsRepetitionActive = true;

            OnRepetitionBegin(RepIndex);
            RepetitionStarted?.Invoke(RepIndex);
        }

        private void FinishExercise()
        {
            IsRunning = false;
            Logger?.LogEvent("exercise_end", details: Summary());
            OnExerciseEnd();
            _feedback?.PlayExerciseComplete();
            OnExerciseCompleted.Invoke();
            SessionContext.SetExercise("");
        }

        private string Summary() => $"success={SuccessCount} miss={MissCount} total={_repetitions}";
    }
}
