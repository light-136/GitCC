// ============================================================
// 文件：MotionViewModel.cs
// 用途：运动控制页面ViewModel
// 设计思路：
//   提供运动控制的核心交互：
//   1. JOG操作 — 手动点动各轴
//   2. 绝对/相对移动 — 指定目标位置运动
//   3. 轴状态监控 — 实时显示所有轴位置和状态
//   4. 回原操作 — 单轴/全部回原
//   5. 急停控制 — 停止选中轴或全部轴
// ============================================================

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Hardware.Motion.Axis;
using SmartSemiCon.Hardware.Motion.Scheduler;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 运动控制页面ViewModel。
    /// </summary>
    public partial class MotionViewModel : ObservableObject
    {
        private readonly AxisManager _axisManager;
        private readonly MotionScheduler _motionScheduler;
        private readonly ILogService _logService;

        [ObservableProperty]
        private int _selectedAxisId;

        [ObservableProperty]
        private double _targetPosition;

        [ObservableProperty]
        private double _jogSpeed = 10.0;

        [ObservableProperty]
        private double _moveSpeed = 50.0;

        [ObservableProperty]
        private double _acceleration = 200.0;

        [ObservableProperty]
        private double _deceleration = 200.0;

        // ---- 插补运动参数 ----
        [ObservableProperty]
        private int _interpAxisX;

        [ObservableProperty]
        private int _interpAxisY = 1;

        [ObservableProperty]
        private double _interpDistance = 50.0;

        // ---- 循环运动参数 ----
        [ObservableProperty]
        private double _cycleStart;

        [ObservableProperty]
        private double _cycleEnd = 100.0;

        [ObservableProperty]
        private int _cycleCount = 3;

        [ObservableProperty]
        private bool _isCycling;

        [ObservableProperty]
        private string _cycleStatus = "就绪";

        private CancellationTokenSource? _cycleCts;

        /// <summary>轴状态集合</summary>
        public ObservableCollection<AxisStatus> AxisStatuses { get; } = new();

        /// <summary>运动日志</summary>
        public ObservableCollection<string> MotionLogs { get; } = new();

        /// <summary>示教点位列表</summary>
        public ObservableCollection<TeachPoint> TeachPoints { get; } = new();

        /// <summary>轨迹记录</summary>
        public ObservableCollection<TrajectoryRecord> Trajectory { get; } = new();

        public MotionViewModel(AxisManager axisManager, MotionScheduler motionScheduler, ILogService logService)
        {
            _axisManager = axisManager;
            _motionScheduler = motionScheduler;
            _logService = logService;
        }

        /// <summary>刷新轴状态</summary>
        [RelayCommand]
        private void RefreshStatus()
        {
            var statuses = _axisManager.GetAllStatus();
            AxisStatuses.Clear();
            foreach (var s in statuses) AxisStatuses.Add(s);
        }

        /// <summary>JOG正向</summary>
        [RelayCommand]
        private async Task JogPositive()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.JogAsync(JogSpeed, true);
            AddLog($"轴{SelectedAxisId} JOG正向 速度={JogSpeed}mm/s");
        }

        /// <summary>JOG负向</summary>
        [RelayCommand]
        private async Task JogNegative()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.JogAsync(JogSpeed, false);
            AddLog($"轴{SelectedAxisId} JOG负向 速度={JogSpeed}mm/s");
        }

        /// <summary>停止JOG</summary>
        [RelayCommand]
        private async Task StopJog()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.StopAsync();
            AddLog($"轴{SelectedAxisId} 停止");
        }

        /// <summary>绝对移动</summary>
        [RelayCommand]
        private async Task MoveAbsolute()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.MoveAbsoluteAsync(TargetPosition, MoveSpeed, Acceleration, Deceleration);
            AddLog($"轴{SelectedAxisId} 绝对移动到 {TargetPosition:F3}mm 速度={MoveSpeed}mm/s");
        }

        /// <summary>相对移动</summary>
        [RelayCommand]
        private async Task MoveRelative()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.MoveRelativeAsync(TargetPosition, MoveSpeed, Acceleration, Deceleration);
            AddLog($"轴{SelectedAxisId} 相对移动 {TargetPosition:F3}mm 速度={MoveSpeed}mm/s");
        }

        /// <summary>单轴回原</summary>
        [RelayCommand]
        private async Task HomeAxis()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.HomeAsync();
            AddLog($"轴{SelectedAxisId} 回原完成");
        }

        /// <summary>全部回原</summary>
        [RelayCommand]
        private async Task HomeAll()
        {
            await _axisManager.HomeAllAsync();
            AddLog("所有轴回原完成");
        }

        /// <summary>使能选中轴</summary>
        [RelayCommand]
        private async Task ServoOn()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;
            await axis.ServoOnAsync();
            AddLog($"轴{SelectedAxisId} 使能");
        }

        /// <summary>全部使能</summary>
        [RelayCommand]
        private async Task ServoOnAll()
        {
            await _axisManager.ServoOnAllAsync();
            AddLog("所有轴已使能");
        }

        /// <summary>全部急停</summary>
        [RelayCommand]
        private async Task EmergencyStopAll()
        {
            await _axisManager.EmergencyStopAllAsync();
            AddLog("全部轴急停！");
        }

        // ============== 新增：插补运动演示 ==============

        /// <summary>直线插补演示 — 两轴同步直线运动</summary>
        [RelayCommand]
        private async Task LinearInterpolation()
        {
            var axisX = _axisManager.GetAxis(InterpAxisX);
            var axisY = _axisManager.GetAxis(InterpAxisY);
            if (axisX == null || axisY == null) { AddLog("插补轴未找到"); return; }

            var targetX = axisX.Status.Position + InterpDistance;
            var targetY = axisY.Status.Position + InterpDistance;
            AddLog($"直线插补: 轴{InterpAxisX}→{targetX:F1}mm, 轴{InterpAxisY}→{targetY:F1}mm");

            await _axisManager.LinearMoveAsync(
                new[] { InterpAxisX, InterpAxisY },
                new[] { targetX, targetY },
                MoveSpeed);

            RecordTrajectory(InterpAxisX, targetX);
            RecordTrajectory(InterpAxisY, targetY);
            AddLog("直线插补完成");
        }

        /// <summary>圆弧插补演示 — 两轴画1/4圆弧</summary>
        [RelayCommand]
        private async Task ArcInterpolation()
        {
            var configs = AxisManager.CreateDefaultConfigs();
            var cardId = configs.FirstOrDefault(c => c.AxisId == InterpAxisX)?.CardId ?? 0;

            var axisX = _axisManager.GetAxis(InterpAxisX);
            var axisY = _axisManager.GetAxis(InterpAxisY);
            if (axisX == null || axisY == null) { AddLog("插补轴未找到"); return; }

            var cx = axisX.Status.Position;
            var cy = axisY.Status.Position + InterpDistance;
            var ex = cx + InterpDistance;
            var ey = cy;

            AddLog($"圆弧插补: 圆心({cx:F1},{cy:F1}), 终点({ex:F1},{ey:F1})");

            var steps = 18;
            var startAngle = -Math.PI / 2;
            var endAngle = 0.0;
            for (int i = 1; i <= steps; i++)
            {
                var angle = startAngle + (endAngle - startAngle) * i / steps;
                var px = cx + InterpDistance * Math.Cos(angle);
                var py = cy + InterpDistance * Math.Sin(angle);
                await _axisManager.LinearMoveAsync(
                    new[] { InterpAxisX, InterpAxisY },
                    new[] { px, py },
                    MoveSpeed);
            }

            RecordTrajectory(InterpAxisX, ex);
            RecordTrajectory(InterpAxisY, ey);
            AddLog("圆弧插补完成");
        }

        // ============== 新增：循环运动 ==============

        /// <summary>启动循环运动 — 选中轴在两点间自动往返</summary>
        [RelayCommand]
        private async Task StartCycle()
        {
            if (IsCycling) return;
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) { AddLog("轴未找到"); return; }

            IsCycling = true;
            _cycleCts = new CancellationTokenSource();
            var token = _cycleCts.Token;

            AddLog($"轴{SelectedAxisId} 循环运动: {CycleStart:F1}→{CycleEnd:F1} 次数={CycleCount}");

            for (int i = 0; i < CycleCount && !token.IsCancellationRequested; i++)
            {
                CycleStatus = $"循环 {i + 1}/{CycleCount} 正向...";
                await axis.MoveAbsoluteAsync(CycleEnd, MoveSpeed, Acceleration, Deceleration, token);
                RecordTrajectory(SelectedAxisId, CycleEnd);

                if (token.IsCancellationRequested) break;

                CycleStatus = $"循环 {i + 1}/{CycleCount} 反向...";
                await axis.MoveAbsoluteAsync(CycleStart, MoveSpeed, Acceleration, Deceleration, token);
                RecordTrajectory(SelectedAxisId, CycleStart);
            }

            CycleStatus = token.IsCancellationRequested ? "已停止" : "完成";
            IsCycling = false;
            AddLog($"轴{SelectedAxisId} 循环运动{CycleStatus}");
        }

        /// <summary>停止循环运动</summary>
        [RelayCommand]
        private void StopCycle()
        {
            _cycleCts?.Cancel();
            var axis = _axisManager.GetAxis(SelectedAxisId);
            axis?.StopAsync();
        }

        // ============== 新增：点位示教 ==============

        /// <summary>示教当前位置 — 保存当前轴位置到示教列表</summary>
        [RelayCommand]
        private void TeachCurrentPosition()
        {
            var axis = _axisManager.GetAxis(SelectedAxisId);
            if (axis == null) return;

            var point = new TeachPoint
            {
                Index = TeachPoints.Count + 1,
                AxisId = SelectedAxisId,
                Position = axis.Status.Position,
                Velocity = MoveSpeed,
                TeachTime = DateTime.Now
            };
            TeachPoints.Add(point);
            AddLog($"示教点#{point.Index}: 轴{SelectedAxisId} 位置={point.Position:F3}mm");
        }

        /// <summary>执行示教点位序列 — 按顺序移动到所有示教位置</summary>
        [RelayCommand]
        private async Task RunTeachSequence()
        {
            if (TeachPoints.Count == 0) { AddLog("示教列表为空"); return; }

            AddLog($"开始执行 {TeachPoints.Count} 个示教点位");
            foreach (var point in TeachPoints)
            {
                var axis = _axisManager.GetAxis(point.AxisId);
                if (axis == null) continue;

                AddLog($"  移动到 #{point.Index}: 轴{point.AxisId}→{point.Position:F3}mm");
                await axis.MoveAbsoluteAsync(point.Position, point.Velocity, Acceleration, Deceleration);
                RecordTrajectory(point.AxisId, point.Position);
            }
            AddLog("示教序列执行完成");
        }

        /// <summary>清空示教列表</summary>
        [RelayCommand]
        private void ClearTeachPoints()
        {
            TeachPoints.Clear();
            AddLog("示教列表已清空");
        }

        /// <summary>清空轨迹记录</summary>
        [RelayCommand]
        private void ClearTrajectory()
        {
            Trajectory.Clear();
        }

        // ---- 辅助方法 ----

        private void RecordTrajectory(int axisId, double position)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Trajectory.Insert(0, new TrajectoryRecord
                {
                    Time = DateTime.Now,
                    AxisId = axisId,
                    Position = position
                });
                if (Trajectory.Count > 500) Trajectory.RemoveAt(Trajectory.Count - 1);
            });
        }

        private void AddLog(string message)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                MotionLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                if (MotionLogs.Count > 200) MotionLogs.RemoveAt(MotionLogs.Count - 1);
            });
            _logService.Log(LogLevel.Info, "运动控制", message);
        }
    }

    /// <summary>示教点位 — 保存单轴位置和运动参数</summary>
    public class TeachPoint
    {
        public int Index { get; set; }
        public int AxisId { get; set; }
        public double Position { get; set; }
        public double Velocity { get; set; }
        public DateTime TeachTime { get; set; }
    }

    /// <summary>轨迹记录 — 运动过程中的位置快照</summary>
    public class TrajectoryRecord
    {
        public DateTime Time { get; set; }
        public int AxisId { get; set; }
        public double Position { get; set; }
    }
}
