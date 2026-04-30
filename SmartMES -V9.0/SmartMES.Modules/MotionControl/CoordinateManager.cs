// ============================================================
// 文件：CoordinateManager.cs
// 用途：坐标系管理器 — 机械/工件/工具坐标系变换
// 设计思路：
//   实现 ICoordinateManager 接口，管理 G54~G59 工件坐标系
//   和工具坐标系的偏移与旋转参数。
//   变换采用 2D 仿射变换（平移+旋转），线程安全。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 坐标系管理器 — 实现机械坐标与工件/工具坐标之间的变换。
    /// 支持 G54~G59 工件坐标系和工具坐标系。
    /// </summary>
    public class CoordinateManager : ICoordinateManager
    {
        // 存储各坐标系的变换参数
        private readonly Dictionary<CoordinateSystem, CoordinateTransform> _offsets = new();
        private readonly object _lock = new();

        /// <summary>
        /// 构造函数 — 初始化所有坐标系为零偏移。
        /// </summary>
        public CoordinateManager()
        {
            // 初始化所有工件坐标系和工具坐标系
            foreach (CoordinateSystem cs in Enum.GetValues<CoordinateSystem>())
            {
                _offsets[cs] = new CoordinateTransform();
            }
        }

        /// <summary>
        /// 设置指定坐标系的偏移参数。
        /// </summary>
        public void SetWorkOffset(CoordinateSystem cs, CoordinateTransform transform)
        {
            lock (_lock)
            {
                _offsets[cs] = transform;
            }
        }

        /// <summary>
        /// 获取指定坐标系的偏移参数。
        /// </summary>
        public CoordinateTransform GetWorkOffset(CoordinateSystem cs)
        {
            lock (_lock)
            {
                return _offsets.TryGetValue(cs, out var t) ? t : new CoordinateTransform();
            }
        }

        /// <summary>
        /// 将机械坐标转换为工件坐标。
        /// 公式：
        ///   dx = mx - offsetX
        ///   dy = my - offsetY
        ///   wx = dx × cos(θ) + dy × sin(θ)
        ///   wy = -dx × sin(θ) + dy × cos(θ)
        ///   wz = mz - offsetZ
        /// </summary>
        public (double X, double Y, double Z) MachineToWork(
            double mx, double my, double mz, CoordinateSystem cs)
        {
            lock (_lock)
            {
                var t = _offsets.GetValueOrDefault(cs, new CoordinateTransform());

                // 平移
                double dx = mx - t.OffsetX;
                double dy = my - t.OffsetY;
                double dz = mz - t.OffsetZ;

                // 旋转（角度转弧度）
                double rad = t.RotationDeg * Math.PI / 180.0;
                double cosR = Math.Cos(rad);
                double sinR = Math.Sin(rad);

                double wx = dx * cosR + dy * sinR;
                double wy = -dx * sinR + dy * cosR;

                return (wx, wy, dz);
            }
        }

        /// <summary>
        /// 将工件坐标转换为机械坐标（逆变换）。
        /// 公式：
        ///   dx = wx × cos(θ) - wy × sin(θ)
        ///   dy = wx × sin(θ) + wy × cos(θ)
        ///   mx = dx + offsetX
        ///   my = dy + offsetY
        ///   mz = wz + offsetZ
        /// </summary>
        public (double X, double Y, double Z) WorkToMachine(
            double wx, double wy, double wz, CoordinateSystem cs)
        {
            lock (_lock)
            {
                var t = _offsets.GetValueOrDefault(cs, new CoordinateTransform());

                // 逆旋转
                double rad = t.RotationDeg * Math.PI / 180.0;
                double cosR = Math.Cos(rad);
                double sinR = Math.Sin(rad);

                double dx = wx * cosR - wy * sinR;
                double dy = wx * sinR + wy * cosR;

                // 逆平移
                double mx = dx + t.OffsetX;
                double my = dy + t.OffsetY;
                double mz = wz + t.OffsetZ;

                return (mx, my, mz);
            }
        }
    }
}
