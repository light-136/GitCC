// GD32低压驱动器Boot升级协议 - CRC16校验算法
// 采用CRC16/Modbus算法，多项式0xA001，初始值0xFFFF

namespace GD32FirmwareUpdater.Protocol
{
    /// <summary>
    /// CRC16校验计算器（Modbus标准）
    /// 算法说明：
    /// 1. 初始CRC值为0xFFFF
    /// 2. 逐字节与CRC异或
    /// 3. 对每一位进行移位和多项式判断
    /// 4. 输出2字节校验值（小端模式存储）
    /// </summary>
    public static class CrcCalculator
    {
        // CRC16/Modbus 多项式（反转形式）
        private const ushort POLYNOMIAL = 0xA001;

        // CRC初始值
        private const ushort INITIAL_VALUE = 0xFFFF;

        /// <summary>
        /// 计算给定数据的CRC16校验值
        /// </summary>
        /// <param name="data">待校验的数据字节数组</param>
        /// <returns>CRC16校验值</returns>
        public static ushort Calculate(byte[] data)
        {
            return Calculate(data, 0, data.Length);
        }

        /// <summary>
        /// 计算指定范围数据的CRC16校验值
        /// </summary>
        /// <param name="data">数据字节数组</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="length">计算长度</param>
        /// <returns>CRC16校验值</returns>
        public static ushort Calculate(byte[] data, int offset, int length)
        {
            ushort crc = INITIAL_VALUE;

            for (int i = offset; i < offset + length; i++)
            {
                // 当前字节与CRC低字节异或
                crc ^= data[i];

                // 对8个bit逐位处理
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        // 最低位为1：右移一位后与多项式异或
                        crc = (ushort)((crc >> 1) ^ POLYNOMIAL);
                    }
                    else
                    {
                        // 最低位为0：仅右移一位
                        crc = (ushort)(crc >> 1);
                    }
                }
            }

            return crc;
        }

        /// <summary>
        /// 将CRC值转为小端字节数组（低字节在前）
        /// </summary>
        public static byte[] ToLittleEndianBytes(ushort crc)
        {
            return new byte[]
            {
                (byte)(crc & 0xFF),        // 低字节
                (byte)((crc >> 8) & 0xFF)  // 高字节
            };
        }
    }
}
