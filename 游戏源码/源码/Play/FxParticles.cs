using System;
using System.Collections.Generic;
using System.Drawing;

namespace ChartPlayer
{
    /// <summary>音符碎片粒子（小四边形沿随机方向飞散并受重力下落）。</summary>
    public struct Particle
    {
        public float X, Y;          // 位置（屏幕坐标）
        public float VX, VY;        // 速度（px/s）
        public float Life, MaxLife; // 剩余/总寿命（ms）
        public float Gravity;       // 重力加速度（px/s²）
        public Color Color;
    }

    /// <summary>命中扩散圆环（半径随时间变大并淡出）。</summary>
    public struct HitRing
    {
        public float X, Y;
        public float R0, R1;        // 起始/结束半径
        public float Life, MaxLife; // 剩余/总寿命（ms）
        public Color Color;
        public bool Miss;
    }

    /// <summary>打击特效粒子系统（纯数据 + 更新逻辑，渲染在 GamePanel）。</summary>
    public static class FxParticles
    {
        /// <summary>更新粒子：位移 + 重力，寿命耗尽即移除。</summary>
        public static void Update(List<Particle> ps, double dtMs)
        {
            if (ps == null) return;
            float dt = (float)(dtMs / 1000.0);
            for (int i = ps.Count - 1; i >= 0; i--)
            {
                var p = ps[i];
                p.Life -= (float)dtMs;
                if (p.Life <= 0) { ps.RemoveAt(i); continue; }
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.VY += p.Gravity * dt;
                ps[i] = p;
            }
        }

        /// <summary>更新扩散圆环：仅推进寿命（渲染时按寿命比例算半径/透明度）。</summary>
        public static void UpdateRings(List<HitRing> rs, double dtMs)
        {
            if (rs == null) return;
            for (int i = rs.Count - 1; i >= 0; i--)
            {
                var r = rs[i];
                r.Life -= (float)dtMs;
                if (r.Life <= 0) rs.RemoveAt(i);
                else rs[i] = r;
            }
        }

        /// <summary>在判定点爆出扩散圆环（MISS 用暗红小环）。</summary>
        public static void SpawnRing(List<HitRing> rs, float x, float y, Color c, bool miss)
        {
            if (rs == null || rs.Count > 240) return;
            rs.Add(new HitRing
            {
                X = x, Y = y,
                R0 = miss ? 8 : 14, R1 = miss ? 26 : 62,
                Life = miss ? 220 : 380, MaxLife = miss ? 220 : 380,
                Color = c, Miss = miss
            });
        }

        /// <summary>爆出 count 个碎片粒子（随机方向飞散 + 重力）。</summary>
        public static void SpawnBurst(List<Particle> ps, Random rnd, float x, float y, Color c, int count)
        {
            if (ps == null || rnd == null) return;
            for (int i = 0; i < count && ps.Count < 420; i++)
            {
                double ang = rnd.NextDouble() * Math.PI * 2;
                double sp = 120 + rnd.NextDouble() * 380;
                float life = 600 + (float)rnd.NextDouble() * 400;
                ps.Add(new Particle
                {
                    X = x, Y = y,
                    VX = (float)(Math.Cos(ang) * sp),
                    VY = (float)(Math.Sin(ang) * sp - 120),
                    Life = life, MaxLife = life,
                    Gravity = 620f,
                    Color = c
                });
            }
        }
    }
}
