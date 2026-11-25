using System;
using UnityEngine;

namespace ReHand.Core
{
    /// <summary>
    /// Shared state of the running session (who, which block, where, what).
    /// SessionManager writes it; DataLogger and the exercises read it, so every
    /// log row carries the same context without components referencing each other.
    /// </summary>
    public static class SessionContext
    {
        /// <summary>Participant ID; "test" until the therapist enters a real one.</summary>
        public static string ParticipantId { get; private set; } = "test";

        /// <summary>Arm(s) trained in this session.</summary>
        public static TrainedArm TrainedArm { get; private set; } = TrainedArm.Both;

        /// <summary>Mode required by the current block (None outside blocks, e.g. tutorial-free exploration).</summary>
        public static InputMode BlockMode { get; private set; } = InputMode.None;

        /// <summary>Mode the headset currently reports (hands or controllers in use). Set by InputModeManager.</summary>
        public static InputMode ActiveMode { get; private set; } = InputMode.None;

        /// <summary>Station the participant is currently at (e.g. "dining_table").</summary>
        public static string Station { get; private set; } = "";

        /// <summary>Exercise currently running (e.g. "grasp", "sort", "reach"); empty between exercises.</summary>
        public static string Exercise { get; private set; } = "";

        /// <summary>Raised after any value changes.</summary>
        public static event Action Changed;

        public static void SetParticipant(string participantId, TrainedArm trainedArm)
        {
            ParticipantId = string.IsNullOrWhiteSpace(participantId) ? "test" : participantId.Trim();
            TrainedArm = trainedArm;
            Changed?.Invoke();
        }

        public static void SetBlockMode(InputMode mode)
        {
            if (BlockMode == mode) return;
            BlockMode = mode;
            Changed?.Invoke();
        }

        public static void SetActiveMode(InputMode mode)
        {
            if (ActiveMode == mode) return;
            ActiveMode = mode;
            Changed?.Invoke();
        }

        public static void SetStation(string station)
        {
            station ??= "";
            if (Station == station) return;
            Station = station;
            Changed?.Invoke();
        }

        public static void SetExercise(string exercise)
        {
            exercise ??= "";
            if (Exercise == exercise) return;
            Exercise = exercise;
            Changed?.Invoke();
        }

        /// <summary>True if the given hand is one of the trained arms.</summary>
        public static bool IsTrained(bool isLeft) =>
            TrainedArm == TrainedArm.Both || (isLeft ? TrainedArm == TrainedArm.Left : TrainedArm == TrainedArm.Right);

        // Static state survives Play Mode when domain reload is disabled; reset it explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ParticipantId = "test";
            TrainedArm = TrainedArm.Both;
            BlockMode = InputMode.None;
            ActiveMode = InputMode.None;
            Station = "";
            Exercise = "";
            Changed = null;
        }
    }
}
