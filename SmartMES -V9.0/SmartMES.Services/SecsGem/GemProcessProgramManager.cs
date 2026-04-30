// ============================================================
// 文件：GemProcessProgramManager.cs
// 用途：GEM 工艺程序管理器 — 工艺程序的上传、下载、删除和列表
// 标准：SEMI E30 — 工艺程序管理（PP Management）
// 设计思路：
//   半导体设备的工艺程序（Process Program/Recipe）包含加工参数。
//   主机可以向设备上传程序、从设备下载程序、删除程序、查询程序列表。
//   本管理器以内存字典存储程序，仿真模式下不持久化到文件系统。
//
//   对应 SECS-II 消息：
//     S7F1/S7F2   — PP 加载询问（主机问设备是否可以接收）
//     S7F3/S7F4   — PP 发送（主机发送程序到设备）
//     S7F5/S7F6   — PP 请求（设备/主机请求下载程序）
//     S7F17/S7F18 — PP 删除
//     S7F19/S7F20 — PP 目录列表
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// 工艺程序 — 包含程序名称和内容。
    /// </summary>
    public class ProcessProgram
    {
        /// <summary>程序 ID（名称）。</summary>
        public string ProgramId { get; set; } = "";

        /// <summary>程序内容（二进制或文本）。</summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>创建/上传时间。</summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>最后修改时间。</summary>
        public DateTime ModifiedTime { get; set; } = DateTime.Now;

        /// <summary>程序大小（字节）。</summary>
        public int Size => Body.Length;
    }

    /// <summary>
    /// GEM 工艺程序管理器 — 管理设备中的工艺程序（Recipe）。
    ///
    /// 消息处理流程：
    ///   S7F1 询问 → 检查空间/权限 → S7F2 回复(PPGNT)
    ///   S7F3 发送 → 存储程序 → S7F4 回复(ACKC7)
    ///   S7F5 请求 → 查找程序 → S7F6 回复(程序内容)
    ///   S7F17 删除 → 删除程序 → S7F18 回复(ACKC7)
    ///   S7F19 列表 → 返回列表 → S7F20 回复(程序名列表)
    /// </summary>
    public class GemProcessProgramManager
    {
        // 程序存储（程序ID → 程序对象）
        private readonly Dictionary<string, ProcessProgram> _programs = new();
        private readonly object _lock = new();

        // 存储限制
        private readonly int _maxPrograms;
        private readonly long _maxTotalSize;

        /// <summary>程序变更事件（添加/删除/更新）。</summary>
        public event EventHandler<string>? ProgramChanged;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="maxPrograms">最大程序数（默认100）。</param>
        /// <param name="maxTotalSize">最大总存储大小（字节，默认10MB）。</param>
        public GemProcessProgramManager(int maxPrograms = 100, long maxTotalSize = 10 * 1024 * 1024)
        {
            _maxPrograms = maxPrograms;
            _maxTotalSize = maxTotalSize;
        }

        // ========== S7F1/S7F2 — PP 加载询问 ==========

        /// <summary>
        /// 处理 PP 加载询问（S7F1） — 检查是否可以接收新程序。
        /// </summary>
        /// <param name="programId">程序 ID。</param>
        /// <param name="size">程序大小。</param>
        /// <returns>PPGNT 码：0=可以, 1=已存在(将覆盖), 2=空间不足, 3=拒绝。</returns>
        public byte CanAcceptProgram(string programId, int size)
        {
            lock (_lock)
            {
                // 检查空间
                long totalSize = _programs.Values.Sum(p => (long)p.Size);
                if (totalSize + size > _maxTotalSize)
                {
                    Log($"[GEM工艺] 拒绝加载 {programId}：空间不足");
                    return 2; // 空间不足
                }

                if (_programs.Count >= _maxPrograms && !_programs.ContainsKey(programId))
                {
                    Log($"[GEM工艺] 拒绝加载 {programId}：数量已满");
                    return 2;
                }

                if (_programs.ContainsKey(programId))
                {
                    Log($"[GEM工艺] 允许加载 {programId}（将覆盖已有程序）");
                    return 1; // 已存在，将覆盖
                }

                Log($"[GEM工艺] 允许加载 {programId}");
                return 0; // 可以
            }
        }

        // ========== S7F3/S7F4 — PP 发送 ==========

        /// <summary>
        /// 接收并存储工艺程序（S7F3）。
        /// </summary>
        /// <param name="programId">程序 ID。</param>
        /// <param name="body">程序内容。</param>
        /// <returns>ACKC7 码：0=成功, 1=权限不足, 2=空间不足, 5=已有同名程序。</returns>
        public byte StoreProgram(string programId, byte[] body)
        {
            lock (_lock)
            {
                var pp = new ProcessProgram
                {
                    ProgramId = programId,
                    Body = body,
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };

                bool isUpdate = _programs.ContainsKey(programId);
                _programs[programId] = pp;

                Log($"[GEM工艺] {(isUpdate ? "更新" : "存储")}程序：{programId}，大小={body.Length}字节");
                ProgramChanged?.Invoke(this, programId);
                return 0; // 成功
            }
        }

        // ========== S7F5/S7F6 — PP 请求 ==========

        /// <summary>
        /// 获取工艺程序内容（S7F5请求）。
        /// </summary>
        /// <param name="programId">程序 ID。</param>
        /// <returns>程序对象，不存在返回 null。</returns>
        public ProcessProgram? GetProgram(string programId)
        {
            lock (_lock)
            {
                return _programs.GetValueOrDefault(programId);
            }
        }

        /// <summary>
        /// 构建 S7F6 响应消息体。
        /// 格式：
        ///   &lt;L [2]
        ///     &lt;A PPID&gt;
        ///     &lt;B PPBODY&gt;
        ///   &gt;
        /// </summary>
        public SecsItem? BuildPPResponse(string programId)
        {
            lock (_lock)
            {
                if (!_programs.TryGetValue(programId, out var pp))
                    return null;

                var root = SecsItem.CreateList();
                root.Children.Add(SecsItem.CreateAscii(pp.ProgramId));
                root.Children.Add(SecsItem.CreateBinary(pp.Body));
                return root;
            }
        }

        // ========== S7F17/S7F18 — PP 删除 ==========

        /// <summary>
        /// 删除工艺程序。
        /// </summary>
        /// <param name="programIds">要删除的程序 ID 列表。</param>
        /// <returns>ACKC7 码：0=成功, 6=对象不存在。</returns>
        public byte DeletePrograms(List<string> programIds)
        {
            lock (_lock)
            {
                bool allExist = programIds.All(_programs.ContainsKey);

                foreach (var id in programIds)
                {
                    if (_programs.Remove(id))
                    {
                        Log($"[GEM工艺] 删除程序：{id}");
                        ProgramChanged?.Invoke(this, id);
                    }
                }

                return allExist ? (byte)0 : (byte)6;
            }
        }

        /// <summary>
        /// 删除所有工艺程序。
        /// </summary>
        public void DeleteAllPrograms()
        {
            lock (_lock)
            {
                _programs.Clear();
                Log("[GEM工艺] 删除所有程序");
                ProgramChanged?.Invoke(this, "*");
            }
        }

        // ========== S7F19/S7F20 — PP 目录列表 ==========

        /// <summary>
        /// 获取所有程序 ID 列表。
        /// </summary>
        public List<string> GetProgramList()
        {
            lock (_lock)
            {
                return _programs.Keys.ToList();
            }
        }

        /// <summary>
        /// 构建 S7F20 响应（程序目录列表）。
        /// 格式：
        ///   &lt;L [n]
        ///     &lt;A PPID1&gt;
        ///     &lt;A PPID2&gt;
        ///     ...
        ///   &gt;
        /// </summary>
        public SecsItem BuildProgramListResponse()
        {
            lock (_lock)
            {
                var root = SecsItem.CreateList();
                foreach (var id in _programs.Keys.OrderBy(x => x))
                {
                    root.Children.Add(SecsItem.CreateAscii(id));
                }
                return root;
            }
        }

        /// <summary>
        /// 获取存储统计信息。
        /// </summary>
        public (int Count, long TotalSize, int MaxPrograms, long MaxTotalSize) GetStorageInfo()
        {
            lock (_lock)
            {
                long totalSize = _programs.Values.Sum(p => (long)p.Size);
                return (_programs.Count, totalSize, _maxPrograms, _maxTotalSize);
            }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
