using System.Runtime.InteropServices;
using System.Text;

namespace SmartMES.Modules.NativeInterop
{
    /// <summary>Native DLL P/Invoke 声明。</summary>
    public static class NativeMath
    {
        private const string DllName = "SmartMES.Native";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double CalcMean([In] double[] data, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double CalcStdDev([In] double[] data, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SortArray([In, Out] double[] data, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double CalcMedian([In] double[] data, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MatMul([In] double[] A, [In] double[] B, [Out] double[] C, int m, int k, int n);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MatTranspose([In] double[] A, [Out] double[] B, int rows, int cols);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MatAdd([In] double[] A, [In] double[] B, [Out] double[] C, int size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StrToUpper([In, Out] byte[] str);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int StrCount([In] byte[] haystack, [In] byte[] needle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StrReverse([In, Out] byte[] str);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void MovingAvg([In] double[] input, [Out] double[] output, int count, int window);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double EuclideanDist([In] double[] a, [In] double[] b, int dim);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern string GetVersion();
    }

    /// <summary>Native 调用便捷封装。</summary>
    public static class NativeHelper
    {
        private static readonly bool _nativeAvailable;

        static NativeHelper()
        {
            try
            {
                var v = NativeMath.GetVersion();
                _nativeAvailable = !string.IsNullOrEmpty(v);
            }
            catch
            {
                _nativeAvailable = false;
            }
        }

        public static bool IsNativeAvailable => _nativeAvailable;

        public static (double Mean, double StdDev, double Median, double Min, double Max) Statistics(double[] data)
        {
            if (!_nativeAvailable || data.Length == 0)
                return (0, 0, 0, 0, 0);

            double mean = NativeMath.CalcMean(data, data.Length);
            double stddev = NativeMath.CalcStdDev(data, data.Length);
            double median = NativeMath.CalcMedian(data, data.Length);
            var sorted = (double[])data.Clone();
            NativeMath.SortArray(sorted, sorted.Length);
            return (mean, stddev, median, sorted[0], sorted[^1]);
        }

        public static double[] MatrixMultiply(double[] A, double[] B, int m, int k, int n)
        {
            var C = new double[m * n];
            if (_nativeAvailable)
                NativeMath.MatMul(A, B, C, m, k, n);
            return C;
        }

        public static double[] MovingAverage(double[] data, int window)
        {
            var output = new double[data.Length];
            if (_nativeAvailable)
                NativeMath.MovingAvg(data, output, data.Length, window);
            return output;
        }

        public static string ToUpperNative(string input)
        {
            if (!_nativeAvailable) return input.ToUpper();
            var bytes = Encoding.ASCII.GetBytes(input + "\0");
            NativeMath.StrToUpper(bytes);
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public static string GetNativeVersion() => _nativeAvailable ? NativeMath.GetVersion() : "Native DLL 未加载";
    }
}
