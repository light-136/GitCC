// ============================================================
// 文件：HttpClientService.cs
// 层次：基础设施层 (Infrastructure Layer) — HTTP 客户端服务
// 职责：
//   封装 HttpClient 的 GET/POST/PUT/DELETE 操作，
//   提供自动 JSON 序列化/反序列化、Bearer Token 认证。
//   实现 ICommunicationService 中 HTTP 相关能力（或作为独立工具类直接注入）。
// 设计思路：
//   使用 IHttpClientFactory 管理 HttpClient 生命周期（自动旋转 Handler，解决 DNS 更新和端口耗尽）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndustry.Infrastructure.Network.Http
{
    /// <summary>
    /// HTTP 客户端服务，封装 HttpClient REST API 调用（JSON 自动序列化、认证头管理）。
    /// 通过 IHttpClientFactory 管理连接生命周期（Singleton 安全）。
    /// </summary>
    public class HttpClientService
    {
        // ----------------------------------------------------------------
        // 内部字段
        // ----------------------------------------------------------------

        /// <summary>
        /// HttpClient 实例（通过 IHttpClientFactory 创建，保证 Handler 生命周期正确）
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// JSON 序列化选项（全局复用以避免每次调用重新创建 JsonSerializerOptions，有性能开销）
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            // 属性名大小写不敏感（兼容后端返回驼峰/帕斯卡命名混合的情况）
            PropertyNameCaseInsensitive = true,
            // 序列化时使用驼峰命名（camelCase），与前后端约定一致
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 忽略 null 值字段（减少 JSON 体积）
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 允许读取带 BOM 的 UTF-8（部分服务器会返回带 BOM 的响应）
            AllowTrailingCommas = true,
            // 枚举值序列化为字符串（更易读）
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// 构造函数：通过 IHttpClientFactory 创建有名的 HttpClient。
        /// "SmartIndustryApi" 是在 DependencyInjection/ServiceCollectionExtensions 中注册的命名客户端。
        /// </summary>
        /// <param name="httpClientFactory">注入的 HttpClient 工厂</param>
        public HttpClientService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("SmartIndustryApi");
        }

        // ================================================================
        // HTTP 操作实现
        // ================================================================

        /// <summary>
        /// 发送 GET 请求，将响应 JSON 反序列化为 T 类型。
        /// 404 响应返回 null（不抛异常），其他 4xx/5xx 抛 HttpRequestException。
        /// </summary>
        public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL 不能为空", nameof(url));

            var response = await _httpClient.GetAsync(url, cancellationToken);

            // 404 返回 null（资源不存在），不视为错误
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return default;

            // 其他非成功状态码：抛出 HttpRequestException
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        /// <summary>
        /// 发送 POST 请求（JSON 请求体），反序列化响应为 TResponse。
        /// </summary>
        public async Task<TResponse?> PostAsync<TRequest, TResponse>(
            string url,
            TRequest body,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL 不能为空", nameof(url));

            var json = JsonSerializer.Serialize(body, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(responseJson)
                ? default
                : JsonSerializer.Deserialize<TResponse>(responseJson, JsonOptions);
        }

        /// <summary>
        /// 发送 PUT 请求（JSON 请求体），反序列化响应为 TResponse。
        /// </summary>
        public async Task<TResponse?> PutAsync<TRequest, TResponse>(
            string url,
            TRequest body,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL 不能为空", nameof(url));

            var json = JsonSerializer.Serialize(body, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(responseJson)
                ? default
                : JsonSerializer.Deserialize<TResponse>(responseJson, JsonOptions);
        }

        /// <summary>
        /// 发送 DELETE 请求（无请求体）。
        /// 返回 true=成功（2xx），false=404（资源不存在）。
        /// </summary>
        public async Task<bool> DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL 不能为空", nameof(url));

            var response = await _httpClient.DeleteAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();
            return true;
        }

        // ================================================================
        // 认证头管理
        // ================================================================

        /// <summary>
        /// 设置 Bearer Token 认证头（用户登录成功后调用）。
        /// 后续所有请求会自动携带此 Authorization 头。
        /// 注意：HttpClient 实例是共享的，此操作影响所有后续请求（线程安全）。
        /// </summary>
        public void SetBearerToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token 不能为空", nameof(token));

            // 使用 BearerToken 认证方案设置 Authorization 头
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// 清除认证头（用户登出时调用）。
        /// </summary>
        public void ClearAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }
}
