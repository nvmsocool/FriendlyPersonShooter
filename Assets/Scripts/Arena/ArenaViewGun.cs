using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Guns you can see: the one in your hands (it kicks when you fire, flashes at the muzzle, dips to
    /// reload, comes up after a swap and moves to the centre when you zoom), and the ones other players
    /// hold. Each frame is a few boxes with a stripe in its payload's colour.
    /// </summary>
    public partial class ArenaView
    {
        private const float FlashSeconds = 0.05f;
        private const float SwapRaiseSeconds = 0.3f;

        private struct GunPart
        {
            public Vector3 Center, Size;
            /// <summary>0 body, 1 dark detail, 2 payload stripe.</summary>
            public byte Kind;

            public GunPart(float x, float y, float z, float sx, float sy, float sz, byte kind)
            {
                Center = new Vector3(x, y, z);
                Size = new Vector3(sx, sy, sz);
                Kind = kind;
            }
        }

        private Material _gunMat, _gunDarkMat, _flashMat;
        private readonly Dictionary<PayloadKind, Material> _gunAccentMats = new Dictionary<PayloadKind, Material>();
        private readonly List<Matrix4x4> _gunBody = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _gunDark = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _muzzleFlash = new List<Matrix4x4>();
        private readonly Dictionary<PayloadKind, List<Matrix4x4>> _gunAccent = new Dictionary<PayloadKind, List<Matrix4x4>>();
        private float _kick, _flashUntil, _swapAt = -10f, _zoomBlend;
        private int _heldActive = -1;
        private Matrix4x4 _heldPose = Matrix4x4.identity;
        private Vector3 _heldMuzzle;

        private void CreateGunMaterials()
        {
            _gunMat = MakeMat(new Color(0.24f, 0.25f, 0.29f));
            _gunDarkMat = MakeMat(new Color(0.11f, 0.11f, 0.13f));
            _flashMat = MakeGlow(new Color(1f, 0.88f, 0.6f), 3f);
            foreach (PayloadKind p in System.Enum.GetValues(typeof(PayloadKind)))
            {
                _gunAccentMats[p] = MakeGlow(ArenaHud.PayloadColor(p), p == PayloadKind.None ? 0.3f : 1.2f);
                _gunAccent[p] = new List<Matrix4x4>();
            }
        }

        private static readonly GunPart[] PistolShape =
        {
            new GunPart(0f, 0.03f, 0.1f, 0.06f, 0.07f, 0.22f, 0),
            new GunPart(0f, -0.06f, 0.02f, 0.05f, 0.12f, 0.06f, 1),
            new GunPart(0f, 0.068f, 0.1f, 0.062f, 0.012f, 0.16f, 2),
        };
        private static readonly GunPart[] RifleShape =
        {
            new GunPart(0f, 0.02f, 0.18f, 0.07f, 0.09f, 0.5f, 0),
            new GunPart(0f, 0.035f, 0.5f, 0.035f, 0.035f, 0.2f, 1),
            new GunPart(0f, -0.07f, 0.17f, 0.05f, 0.12f, 0.07f, 1),
            new GunPart(0f, 0f, -0.12f, 0.06f, 0.08f, 0.14f, 1),
            new GunPart(0f, 0.068f, 0.18f, 0.072f, 0.012f, 0.4f, 2),
        };
        private static readonly GunPart[] SmgShape =
        {
            new GunPart(0f, 0.02f, 0.14f, 0.08f, 0.1f, 0.36f, 0),
            new GunPart(0f, -0.1f, 0.1f, 0.045f, 0.18f, 0.06f, 1),
            new GunPart(0f, 0.03f, 0.36f, 0.04f, 0.04f, 0.1f, 1),
            new GunPart(0.041f, 0.02f, 0.14f, 0.01f, 0.05f, 0.3f, 2),
        };
        private static readonly GunPart[] MarksmanShape =
        {
            new GunPart(0f, 0.02f, 0.25f, 0.06f, 0.08f, 0.62f, 0),
            new GunPart(0f, 0.03f, 0.66f, 0.03f, 0.03f, 0.26f, 1),
            new GunPart(0f, 0.1f, 0.22f, 0.05f, 0.05f, 0.24f, 1),
            new GunPart(0f, -0.01f, -0.13f, 0.055f, 0.09f, 0.18f, 0),
            new GunPart(0f, 0.062f, 0.25f, 0.062f, 0.01f, 0.5f, 2),
        };
        private static readonly GunPart[] BeamShape =
        {
            new GunPart(0f, 0.02f, 0.15f, 0.12f, 0.12f, 0.4f, 0),
            new GunPart(0f, -0.08f, 0.04f, 0.05f, 0.12f, 0.06f, 1),
            new GunPart(0f, 0.02f, 0.25f, 0.13f, 0.13f, 0.03f, 2),
            new GunPart(0f, 0.02f, 0.39f, 0.08f, 0.08f, 0.08f, 2),
        };
        private static readonly GunPart[] LauncherShape =
        {
            new GunPart(0f, 0.04f, 0.15f, 0.15f, 0.15f, 0.6f, 0),
            new GunPart(0f, -0.08f, 0.05f, 0.05f, 0.12f, 0.06f, 1),
            new GunPart(0f, 0.13f, 0.1f, 0.02f, 0.04f, 0.04f, 1),
            new GunPart(0f, 0.04f, 0.42f, 0.155f, 0.155f, 0.04f, 2),
        };

        private static GunPart[] ShapeOf(FrameKind f, out Vector3 muzzle)
        {
            switch (f)
            {
                case FrameKind.Pistol: muzzle = new Vector3(0f, 0.03f, 0.22f); return PistolShape;
                case FrameKind.Smg: muzzle = new Vector3(0f, 0.03f, 0.41f); return SmgShape;
                case FrameKind.Marksman: muzzle = new Vector3(0f, 0.03f, 0.79f); return MarksmanShape;
                case FrameKind.Beam: muzzle = new Vector3(0f, 0.02f, 0.44f); return BeamShape;
                case FrameKind.Launcher: muzzle = new Vector3(0f, 0.04f, 0.46f); return LauncherShape;
                default: muzzle = new Vector3(0f, 0.035f, 0.6f); return RifleShape;
            }
        }

        /// <summary>How hard each frame kicks the gun back per shot.</summary>
        private static float KickOf(FrameKind f)
        {
            switch (f)
            {
                case FrameKind.Pistol: return 0.55f;
                case FrameKind.Smg: return 0.22f;
                case FrameKind.Marksman: return 1f;
                case FrameKind.Beam: return 0.05f;
                case FrameKind.Launcher: return 1f;
                default: return 0.35f;
            }
        }

        /// <summary>Our own shot left the gun: kick and flash.</summary>
        private void OnLocalShot(FrameKind frame)
        {
            _kick = Mathf.Min(1.2f, _kick + KickOf(frame));
            if (frame != FrameKind.Beam || Time.unscaledTime >= _flashUntil) _flashUntil = Time.unscaledTime + FlashSeconds;
        }

        /// <summary>Where our tracers start: the held gun's muzzle (last frame's pose).</summary>
        private Vector3 HeldMuzzle(Vector3 eye)
        {
            return _heldActive >= 0 ? _heldMuzzle : eye + viewCamera.transform.right * 0.25f - Vector3.up * 0.25f;
        }

        private void AddGun(Matrix4x4 basis, FrameKind frame, PayloadKind payload)
        {
            var shape = ShapeOf(frame, out _);
            foreach (var part in shape)
            {
                var m = basis * Matrix4x4.TRS(part.Center, Quaternion.identity, part.Size);
                (part.Kind == 0 ? _gunBody : part.Kind == 1 ? _gunDark : _gunAccent[payload]).Add(m);
            }
        }

        /// <summary>The gun in your hands, posed in front of the camera.</summary>
        private void AddHeldGun(ArenaLoadout lo, bool alive)
        {
            if (viewCamera == null || !alive)
            {
                _heldActive = -1;
                return;
            }
            var gun = Gun.Unpack(lo.Active == 0 ? lo.GunA : lo.GunB);
            if (gun.IsEmpty)
            {
                _heldActive = -1;
                return;
            }
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            if (lo.Active != _heldActive)
            {
                if (_heldActive >= 0) _swapAt = now;
                _heldActive = lo.Active;
            }
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 6f);
            _zoomBlend = Mathf.MoveTowards(_zoomBlend, ArenaLocalInputSystem.Zooming ? 1f : 0f, dt * 7f);

            var hip = new Vector3(0.16f, -0.15f, 0.34f);
            var aimed = new Vector3(0f, -0.085f, 0.28f);
            var pos = Vector3.Lerp(hip, aimed, _zoomBlend);
            float pitchUp = _kick * 7f;
            pos.z -= _kick * 0.05f;
            float roll = 0f;
            if (lo.Reload < 0.999f)
            {
                // Dip and tilt while reloading.
                float dip = Mathf.Sin(Mathf.Clamp01(lo.Reload) * Mathf.PI);
                pos.y -= dip * 0.09f;
                pitchUp -= dip * 30f;
                roll = dip * 25f;
            }
            float raise = Mathf.Clamp01((now - _swapAt) / SwapRaiseSeconds);
            pos.y -= (1f - raise) * 0.25f;

            var cam = viewCamera.transform;
            var local = Matrix4x4.TRS(pos, Quaternion.Euler(-pitchUp, -9f * (1f - _zoomBlend), roll), Vector3.one * 0.6f);
            _heldPose = Matrix4x4.TRS(cam.position, cam.rotation, Vector3.one) * local;
            AddGun(_heldPose, gun.Frame, gun.Payload);
            ShapeOf(gun.Frame, out var muzzle);
            _heldMuzzle = _heldPose.MultiplyPoint3x4(muzzle);
            if (now < _flashUntil)
            {
                _muzzleFlash.Add(Matrix4x4.TRS(_heldMuzzle, cam.rotation * Quaternion.Euler(0f, 0f, now * 2000f % 360f), Vector3.one * 0.11f));
            }
        }

        /// <summary>A gun in another player's hands, from their position and where they look.</summary>
        private void AddHeldByOther(Vector3 feet, float yaw, float pitch, ArenaLoadout lo)
        {
            var gun = Gun.Unpack(lo.Active == 0 ? lo.GunA : lo.GunB);
            if (gun.IsEmpty) return;
            var body = Quaternion.Euler(0f, yaw, 0f);
            var hand = feet + Vector3.up * 1.2f + body * new Vector3(0.42f, 0f, 0.25f);
            AddGun(Matrix4x4.TRS(hand, Quaternion.Euler(-pitch, yaw, 0f), Vector3.one * 1.4f), gun.Frame, lo.Payload);
        }

        private void DrawGuns()
        {
            DrawList(_gunBody, _gunMat);
            DrawList(_gunDark, _gunDarkMat);
            foreach (var kv in _gunAccent) DrawList(kv.Value, _gunAccentMats[kv.Key]);
            DrawList(_muzzleFlash, _flashMat);
            _gunBody.Clear();
            _gunDark.Clear();
            _muzzleFlash.Clear();
            foreach (var l in _gunAccent.Values) l.Clear();
        }
    }
}
