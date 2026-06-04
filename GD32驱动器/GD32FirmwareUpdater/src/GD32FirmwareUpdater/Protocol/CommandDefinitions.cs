// GD32低压驱动器Boot升级协议 - 指令常量定义
// 定义协议帧中的固定值和所有指令编码

namespace GD32FirmwareUpdater.Protocol
{
    /// <summary>
    /// 协议帧固定字段常量
    /// </summary>
    public static class FrameConstants
    {
        // 帧头固定值
        public const byte SOF = 0x01;

        // 功能码固定值
        public const byte FUNC = 0x06;

        // 帧头长度: SOF(1) + FUNC(1) + CMD(2) + DATALEN(2) = 6字节
        public const int HEADER_SIZE = 6;

        // CRC校验长度: 2字节
        public const int CRC_SIZE = 2;

        // 升级数据每帧最大有效载荷（协议规定不超过128字节）
        public const int MAX_DATA_PAYLOAD = 128;
    }

    /// <summary>
    /// 协议指令码（CMD字段，大端模式）
    /// </summary>
    public static class CommandCode
    {
        // 升级请求 - 询问驱动器是否具备升级条件
        public const ushort UPGRADE_REQ = 0x0010;

        // 启动升级 - 发送文件大小，驱动器擦除Flash
        public const ushort UPGRADE_START = 0x0011;

        // 发送升级数据 - 将固件数据写入Flash
        public const ushort UPGRADE_DATA = 0x0012;

        // 完成升级 - 驱动器从Boot跳转App
        public const ushort JUMP_APP = 0x0013;

        // 请求版本号 - 查询Bootloader版本号
        public const ushort VER_REQ = 0x0014;
    }

    /// <summary>
    /// 升级请求的子命令（UPGRADE_REQ 的 DATA[0]）
    /// </summary>
    public static class UpgradeRequestType
    {
        // 升级状态请求（请求进入Boot并准备升级）
        public const byte REQUEST_UPGRADE = 0x01;

        // 查询当前处于何种状态
        public const byte QUERY_STATUS = 0x02;
    }

    /// <summary>
    /// 升级请求的回复状态码（UPGRADE_REQ 回复的 DATA[0]）
    /// </summary>
    public static class UpgradeRequestResponse
    {
        // 当前处于Boot中，具备升级条件
        public const byte IN_BOOT_READY = 0x00;

        // 当前处于App中，正在跳转到Boot
        public const byte IN_APP_JUMPING = 0x01;

        // 当前处于App中，只回复状态不做处理
        public const byte IN_APP_STATUS_ONLY = 0x02;

        // 当前处于Boot中，只回复状态不做处理
        public const byte IN_BOOT_STATUS_ONLY = 0x03;
    }

    /// <summary>
    /// 通用回复状态码
    /// </summary>
    public static class ResponseStatus
    {
        // 操作成功
        public const byte SUCCESS = 0x00;

        // 操作失败
        public const byte FAILURE = 0x01;
    }
}
