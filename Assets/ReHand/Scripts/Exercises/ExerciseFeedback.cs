using System;
using UnityEngine;

namespace ReHand.Exercises
{
    /// <summary>
    /// Sound, particles and points for the exercises. Every clip and effect is optional:
    /// leave a slot empty and that feedback is simply skipped, so the exercises work
    /// before the final sounds (Kenney jingles, chimes…) and VFX are imported.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ExerciseFeedback : MonoBehaviour
    {
        [Header("Sounds")]
        [SerializeField] private AudioClip _successClip;
        [Tooltip("Soft sound for a wrong place, a drop or a missed target. Keep it gentle.")]
        [SerializeField] private AudioClip _missClip;
        [SerializeField] private AudioClip _grabClip;
        [SerializeField] private AudioClip _drinkClip;
        [SerializeField] private AudioClip _exerciseCompleteClip;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;

        [Header("Effects")]
        [Tooltip("Particle prefab played on every successful repetition (star burst).")]
        [SerializeField] private ParticleSystem _successBurstPrefab;

        [Header("Score")]
        [SerializeField, Min(0)] private int _pointsPerSuccess = 10;

        public int Score { get; private set; }
        public event Action<int> ScoreChanged;

        private AudioSource _audio;

        protected virtual void Awake()
        {
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
        }

        public void PlaySuccess(Vector3 position)
        {
            Play(_successClip);
            Burst(position);
            AddPoints(_pointsPerSuccess);
        }

        public void PlayMiss(Vector3 position) => Play(_missClip);
        public void PlayGrab(Vector3 position) => Play(_grabClip, 0.6f);
        public void PlayDrink(Vector3 position) => Play(_drinkClip);
        public void PlayExerciseComplete() => Play(_exerciseCompleteClip);

        public void ResetScore()
        {
            Score = 0;
            ScoreChanged?.Invoke(Score);
        }

        private void AddPoints(int points)
        {
            if (points == 0) return;
            Score += points;
            ScoreChanged?.Invoke(Score);
        }

        private void Play(AudioClip clip, float volumeScale = 1f)
        {
            if (clip != null) _audio.PlayOneShot(clip, _volume * volumeScale);
        }

        private void Burst(Vector3 position)
        {
            if (_successBurstPrefab == null) return;
            ParticleSystem ps = Instantiate(_successBurstPrefab, position, Quaternion.identity);
            ps.Play();
            var main = ps.main;
            Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax + 0.5f);
        }
    }
}
