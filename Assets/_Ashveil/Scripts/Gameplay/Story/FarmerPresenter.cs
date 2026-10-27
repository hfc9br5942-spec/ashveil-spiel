using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Quests;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Der Pferdezuechter von Hallgrund, wie DIESER Spieler ihn sieht (lokal, Phasing): Alltag am Hof, Panik am Westzaun,
    /// Warten, Erleichterung, spaeter muede auf der Bank – und am Ende geht er ins Haus (kein Sterben vor anderen).
    /// Der Wurzelknoten (mit NpcIdentity) bleibt fest auf dem Hofplatz, damit der Server die Entfernung sauber pruefen kann;
    /// bewegt wird nur die Figur darunter. Er geht auf den Spieler zu, wenn eine Szene es verlangt (cue "breeder_approach").
    /// </summary>
    public sealed class FarmerPresenter : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int WalkingId = Animator.StringToHash("Walking");

        [SerializeField] private Transform figure;
        [SerializeField] private Animator animator;
        [SerializeField] private EmotePoser poser;
        [Tooltip("Arbeitsplaetze im Alltag (Werkbank, Koppel, Traenke)")]
        [SerializeField] private Transform[] workSpots = new Transform[0];
        [SerializeField] private Transform westFence;
        [SerializeField] private Transform bench;
        [SerializeField] private Transform door;
        [SerializeField] private Transform gateWatch;
        [SerializeField] private float approachTriggerDistance = 26f;

        private Vector3[] _path = new Vector3[0];
        private int _pathIndex;
        private float _walkSpeed = 1.3f;
        private string _activity = "";
        private string _shown = "\u0000";
        private float _nextWork;
        private int _work;
        private bool _approaching, _inside, _approachSent, _goingInside;
        private Transform _player;
        private FarmPhase _phase = (FarmPhase)255;

        public Transform Figure => figure;
        public bool Inside => _inside;
        /// <summary>Tests: laeuft er gerade auf den Spieler zu?</summary>
        public bool Approaching => _approaching;
        public string Activity => _activity;

        public void Bind(Transform fig, Animator anim, EmotePoser emote, Transform[] work, Transform fence, Transform benchSpot, Transform doorSpot, Transform gate)
        {
            figure = fig;
            animator = anim;
            poser = emote;
            workSpots = work;
            westFence = fence;
            bench = benchSpot;
            door = doorSpot;
            gateWatch = gate;
        }

        private void OnEnable()
        {
            StoryDirector.Cue += OnCue;
            StoryDirector.SceneStarted += OnSceneStarted;
            StoryDirector.SceneFinished += OnSceneFinished;
            StoryDirector.RegisterFocus("breeder", () => figure != null ? figure.position + Vector3.up * 1.5f : (Vector3?)null);
        }

        private void OnDisable()
        {
            StoryDirector.Cue -= OnCue;
            StoryDirector.SceneStarted -= OnSceneStarted;
            StoryDirector.SceneFinished -= OnSceneFinished;
            StoryDirector.UnregisterFocus("breeder");
        }

        private void OnSceneStarted(string id)
        {
            if (id == "q1_approach" || id == "q1_home" || id == "q2_recognize") _approachSent = true;
        }

        private float _backAt = float.MaxValue;

        private void OnSceneFinished(string id)
        {
            if (_approaching) _backAt = Time.time + 12f; // nach dem Gespraech zurueck an seinen Platz
        }

        /// <summary>Zurueck an den Platz der aktuellen Phase (gehend).</summary>
        private void ReturnToPost()
        {
            _approaching = false;
            _backAt = float.MaxValue;
            Transform post = _phase == FarmPhase.Crisis ? westFence : _phase == FarmPhase.Return ? gateWatch : _phase == FarmPhase.Aged ? bench : null;
            if (post == null) { _nextWork = 0f; return; }
            _activity = _phase == FarmPhase.Aged ? "Sit" : _phase == FarmPhase.Crisis ? "FoldArms" : "Stand";
            WalkTo(post.position, _walkSpeed, false, post.eulerAngles.y);
        }

        private void OnCue(string cue)
        {
            switch (cue)
            {
                case "breeder_approach":
                    if (LocalPlayer() != null) WalkTo(_player.position + (figure.position - _player.position).normalized * 2.6f, 2.1f, true);
                    break;
                case "breeder_wave":
                    if (poser != null) poser.Play(EmoteId.Wave, 0f);
                    _holdUntil = Time.time + 2.2f;
                    _shown = "\u0001";
                    break;
                case "breeder_point_west":
                    Show("Talk");
                    break;
                case "breeder_go_inside":
                    if (door != null) { _goingInside = true; WalkTo(door.position, 1.0f, false); }
                    break;
            }
        }

        private Transform LocalPlayer()
        {
            _player = LocalPlayerRef.Transform;
            return _player;
        }

        private void Update()
        {
            if (figure == null) return;
            FarmPhase phase = StoryClient.Ready ? StoryClient.Farm : FarmPhase.Normal;
            if (phase != _phase) EnterPhase(phase);

            // Q1: der Zuechter bemerkt Ruestung und Schild – und kommt herueber (der Server entscheidet, ob die Szene kommt)
            if (!_approachSent && StoryClient.Ready && StoryClient.HorseState == Ashveil.Data.HorseStoryState.Unknown &&
                (phase == FarmPhase.Crisis || phase == FarmPhase.Aged) && LocalPlayer() != null &&
                Vector3.Distance(_player.position, figure.position) < approachTriggerDistance && !StoryDirector.Active)
            {
                _approachSent = true;
                StoryClient.Send("approach", "");
            }
            if (phase == FarmPhase.Aged && StoryClient.Ready && StoryClient.HorseState == Ashveil.Data.HorseStoryState.AdultAtFarm &&
                !_approachSent && LocalPlayer() != null && Vector3.Distance(_player.position, figure.position) < approachTriggerDistance + 8f && !StoryDirector.Active)
            {
                _approachSent = true;
                StoryClient.Send("approach", "");
            }

            if (_approaching && Time.time > _backAt && !StoryDirector.Active && (ProgressionClient.Current == null || ProgressionClient.Current.DialogNpc == null))
                ReturnToPost();
            if (_path.Length > 0) StepPath(Time.deltaTime);
            else
            {
                Animate(0f);
                if (_approaching && LocalPlayer() != null) Face(_player.position);
                if (phase == FarmPhase.Normal || phase == FarmPhase.Growing) Work();
                if (_approaching && _activity != "Talk") _activity = "Talk";
                if (Time.time >= _holdUntil) Show(_activity);
            }
        }

        private void EnterPhase(FarmPhase p)
        {
            _phase = p;
            _approaching = false;
            _goingInside = false;
            _approachSent = false;
            bool hidden = p == FarmPhase.After || p == FarmPhase.Epilogue;
            SetInside(hidden);
            if (hidden) return;
            switch (p)
            {
                case FarmPhase.Crisis:
                    Teleport(westFence != null ? westFence : transform);
                    _activity = "FoldArms";
                    _walkSpeed = 1.6f;
                    break;
                case FarmPhase.Return:
                    Teleport(gateWatch != null ? gateWatch : transform);
                    _activity = "Stand";
                    break;
                case FarmPhase.Aged:
                    Teleport(bench != null ? bench : transform);
                    _activity = "Sit";
                    _walkSpeed = 0.85f;
                    break;
                default:
                    _walkSpeed = 1.25f;
                    _nextWork = 0f;
                    break;
            }
        }

        private void Work()
        {
            if (workSpots == null || workSpots.Length == 0 || Time.time < _nextWork) return;
            _nextWork = Time.time + 40f + (_work % 3) * 9f;
            _work = (_work + 1) % workSpots.Length;
            Transform s = workSpots[_work];
            _activity = s.name.Contains("Werkbank") ? "Chop" : s.name.Contains("Traenke") ? "Kneel" : "FoldArms";
            WalkTo(s.position, _walkSpeed, false, s.eulerAngles.y);
        }

        private float _endYaw = float.NaN;
        private float _holdUntil;

        private void WalkTo(Vector3 target, float speed, bool approach, float endYaw = float.NaN)
        {
            _approaching = approach;
            _walkSpeed = speed;
            _endYaw = endYaw;
            Vector3 from = figure.position;
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(target, out NavMeshHit b, 2.5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 0)
                _path = path.corners;
            else _path = new[] { target };
            _pathIndex = 0;
        }

        private void StepPath(float dt)
        {
            Vector3 p = figure.position;
            Vector3 t = _path[_pathIndex];
            Vector3 d = t - p;
            d.y = 0f;
            float step = _walkSpeed * dt;
            if (d.magnitude <= step + 0.05f)
            {
                figure.position = t;
                _pathIndex++;
                if (_pathIndex >= _path.Length)
                {
                    _path = new Vector3[0];
                    if (!float.IsNaN(_endYaw)) figure.rotation = Quaternion.Euler(0f, _endYaw, 0f);
                    if (_goingInside) SetInside(true);
                }
                return;
            }
            Vector3 np = p + d.normalized * step;
            if (Ashveil.Core.WorldGround.TrySample(np, out float h)) np.y = h;
            figure.position = np;
            figure.rotation = Quaternion.Slerp(figure.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * dt));
            Animate(_walkSpeed);
            Show(null);
        }

        private void Face(Vector3 target)
        {
            Vector3 d = target - figure.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            figure.rotation = Quaternion.Slerp(figure.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        private void Teleport(Transform spot)
        {
            _path = new Vector3[0];
            figure.SetPositionAndRotation(spot.position, spot.rotation);
        }

        private void SetInside(bool inside)
        {
            _inside = inside;
            for (int i = 0; i < figure.childCount; i++) figure.GetChild(i).gameObject.SetActive(!inside);
            var col = figure.GetComponent<Collider>();
            if (col != null) col.enabled = !inside;
        }

        private void Animate(float speed)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(MoveZId, speed > 0.05f ? 1f : 0f);
            animator.SetBool(GroundedId, true);
            animator.SetBool(WalkingId, speed > 0.05f);
        }

        private void Show(string act)
        {
            if (poser == null || act == _shown) return;
            _shown = act;
            switch (act)
            {
                case null: case "": case "Stand": poser.PlayActivity(null); break;
                case "Sit": poser.Play(EmoteId.Sit, 1f, 0.45f); break;
                case "Wave": poser.Play(EmoteId.Wave, 0f); _shown = "\u0001"; break;
                default: poser.PlayActivity(act); break;
            }
        }
    }
}
