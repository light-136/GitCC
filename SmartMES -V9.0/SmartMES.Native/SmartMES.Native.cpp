#ifdef _WIN32
    #define EXPORT __declspec(dllexport)
#else
    #define EXPORT __attribute__((visibility("default")))
#endif

#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <stdint.h>

extern "C" {

// ======================== 统计计算 ========================

/// 计算均值
EXPORT double CalcMean(const double* data, int count)
{
    if (!data || count <= 0) return 0.0;
    double sum = 0.0;
    for (int i = 0; i < count; i++) sum += data[i];
    return sum / count;
}

/// 计算标准差（样本标准差）
EXPORT double CalcStdDev(const double* data, int count)
{
    if (!data || count <= 1) return 0.0;
    double mean = CalcMean(data, count);
    double v = 0.0;
    for (int i = 0; i < count; i++) { double d = data[i] - mean; v += d * d; }
    return sqrt(v / (count - 1));
}

static void QuickSort(double* arr, int l, int r)
{
    if (l >= r) return;
    double p = arr[(l + r) / 2];
    int i = l, j = r;
    while (i <= j)
    {
        while (arr[i] < p) i++;
        while (arr[j] > p) j--;
        if (i <= j) { double t = arr[i]; arr[i] = arr[j]; arr[j] = t; i++; j--; }
    }
    QuickSort(arr, l, j);
    QuickSort(arr, i, r);
}

/// 原地快速排序
EXPORT void SortArray(double* data, int count)
{
    if (data && count > 1) QuickSort(data, 0, count - 1);
}

/// 计算中位数
EXPORT double CalcMedian(double* data, int count)
{
    if (!data || count <= 0) return 0.0;
    double* tmp = (double*)malloc(count * sizeof(double));
    if (!tmp) return 0.0;
    memcpy(tmp, data, count * sizeof(double));
    QuickSort(tmp, 0, count - 1);
    double m = (count % 2 == 0)
        ? (tmp[count/2 - 1] + tmp[count/2]) / 2.0
        : tmp[count/2];
    free(tmp);
    return m;
}

// ======================== 矩阵运算 ========================
// 行主序: M[i][j] = data[i*cols + j]

/// 矩阵乘法 C(m x n) = A(m x k) * B(k x n)
EXPORT void MatMul(const double* A, const double* B, double* C, int m, int k, int n)
{
    if (!A || !B || !C) return;
    for (int i = 0; i < m; i++)
        for (int j = 0; j < n; j++)
        {
            double s = 0.0;
            for (int p = 0; p < k; p++) s += A[i*k + p] * B[p*n + j];
            C[i*n + j] = s;
        }
}

/// 矩阵转置 B(n x m) = A(m x n)^T
EXPORT void MatTranspose(const double* A, double* B, int rows, int cols)
{
    if (!A || !B) return;
    for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
            B[j*rows + i] = A[i*cols + j];
}

/// 矩阵加法 C = A + B (相同维度)
EXPORT void MatAdd(const double* A, const double* B, double* C, int size)
{
    for (int i = 0; i < size; i++) C[i] = A[i] + B[i];
}

// ======================== 字符串处理 ========================

/// 字符串转大写（原地）
EXPORT void StrToUpper(char* str)
{
    if (!str) return;
    for (; *str; str++) if (*str >= 'a' && *str <= 'z') *str -= 32;
}

/// 统计子串出现次数
EXPORT int StrCount(const char* haystack, const char* needle)
{
    if (!haystack || !needle || !*needle) return 0;
    int cnt = 0;
    size_t nlen = strlen(needle);
    const char* p = haystack;
    while ((p = strstr(p, needle)) != NULL) { cnt++; p += nlen; }
    return cnt;
}

/// 字符串反转（原地）
EXPORT void StrReverse(char* str)
{
    if (!str) return;
    int len = (int)strlen(str);
    for (int i = 0, j = len - 1; i < j; i++, j--)
    { char t = str[i]; str[i] = str[j]; str[j] = t; }
}

// ======================== 高性能信号处理 ========================

/// 移动平均滤波（窗口大小 window）
EXPORT void MovingAvg(const double* in, double* out, int count, int window)
{
    if (!in || !out || count <= 0 || window <= 0) return;
    for (int i = 0; i < count; i++)
    {
        int start = (i - window + 1 > 0) ? i - window + 1 : 0;
        double s = 0; int n = 0;
        for (int j = start; j <= i; j++) { s += in[j]; n++; }
        out[i] = s / n;
    }
}

/// 计算向量欧式距离
EXPORT double EuclideanDist(const double* a, const double* b, int dim)
{
    double s = 0;
    for (int i = 0; i < dim; i++) { double d = a[i] - b[i]; s += d * d; }
    return sqrt(s);
}

/// 版本信息（测试P/Invoke连通性）
EXPORT const char* GetVersion() { return "SmartMES.Native v1.0 (C++)"; }

} // extern "C"
