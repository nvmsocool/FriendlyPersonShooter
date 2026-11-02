using System.Collections.Generic;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// A bot's route over the generated zones: plans a path to where it wants to go, hands out the next
    /// waypoint to steer at, says when to jump up a ledge, and replans when the goal moves or it gets stuck.
    /// </summary>
    public sealed class BotPath
    {
        private const float ReplanInterval = 4f;
        private const float ReachRadius = 1.1f;
        private const float StuckTime = 1.5f;

        private List<UnityEngine.Vector3> _points;
        private int _next;
        private float3 _goal;
        private float _replanIn, _stuckFor;
        private int _stuckCount;
        private float3 _lastPos;
        private bool _complete;

        /// <summary>The point to steer at on the way to <paramref name="goal"/> (its nearest copy to <paramref name="pos"/>).</summary>
        public float3 Steer(float dt, float3 pos, float3 goal, out bool jump)
        {
            jump = false;
            var world = ExpeditionTerrain.World;
            _replanIn -= dt;

            // Stuck: hop, and plan again.
            if (math.distance(pos.xz, _lastPos.xz) < 0.6f * dt * ArenaSettings.MoveSpeed) _stuckFor += dt;
            else _stuckFor = 0f;
            _lastPos = pos;
            if (_stuckFor > StuckTime)
            {
                // Hop; the second time in a row, skip this waypoint; the third, plan again.
                jump = true;
                _stuckFor = 0f;
                _stuckCount++;
                if (_stuckCount == 2 && _points != null && _next < _points.Count - 1) _next++;
                if (_stuckCount >= 3)
                {
                    _stuckCount = 0;
                    _replanIn = 0f;
                }
            }
            else if (_stuckFor == 0f) _stuckCount = 0;

            if (_points == null || _replanIn <= 0f || math.distance(_goal.xz, goal.xz) > 8f || _next >= _points.Count)
            {
                _goal = goal;
                _replanIn = _complete ? ReplanInterval : ReplanInterval * 0.5f;
                _points = world.Navigator.FindPath(pos, goal, out _complete);
                _next = 0;
            }
            if (_points.Count == 0) return goal;

            // Skip waypoints already reached.
            while (_next < _points.Count - 1)
            {
                var w = ExpeditionWorld.Near(_points[_next], pos);
                if (math.distance(w.xz, pos.xz) > ReachRadius || math.abs(w.y - pos.y) > 1.6f) break;
                _next++;
            }
            var target = ExpeditionWorld.Near(_points[_next], pos);
            if (target.y > pos.y + ExpeditionTerrain.StepHeight && math.distance(target.xz, pos.xz) < 2.5f) jump = true;
            return target;
        }
    }
}
