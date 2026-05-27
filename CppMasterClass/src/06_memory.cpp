// ============================================================
// 06_memory.cpp — 内存管理
// 覆盖：RAII模式、unique_ptr、shared_ptr、weak_ptr、
//       移动语义、完美转发、自定义内存追踪
// ============================================================

#include <iostream>
#include <memory>
#include <string>
#include <vector>
#include <utility>

// ---- 全局内存追踪 ----
static int g_alloc_count = 0;
static int g_free_count = 0;
static size_t g_total_bytes = 0;

// ---- 1. RAII 模式 — 资源获取即初始化 ----
class FileHandle {
    std::string filename_;
    bool is_open_ = false;

public:
    FileHandle(const std::string& filename) : filename_(filename) {
        is_open_ = true;
        std::cout << "  [RAII] 文件打开: " << filename_ << "\n";
    }

    ~FileHandle() {
        if (is_open_) {
            is_open_ = false;
            std::cout << "  [RAII] 文件关闭: " << filename_ << " (析构时自动关闭)\n";
        }
    }

    // 禁止拷贝(文件句柄不应该被拷贝)
    FileHandle(const FileHandle&) = delete;
    FileHandle& operator=(const FileHandle&) = delete;

    // 允许移动
    FileHandle(FileHandle&& other) noexcept
        : filename_(std::move(other.filename_)), is_open_(other.is_open_) {
        other.is_open_ = false;
        std::cout << "  [RAII] 文件句柄已转移: " << filename_ << "\n";
    }

    void write(const std::string& data) {
        if (is_open_) {
            std::cout << "  [写入] " << filename_ << ": " << data << "\n";
        }
    }
};

// ---- 2. 带内存追踪的类 ----
class TrackedObject {
    std::string name_;
    int* data_;
    size_t size_;

public:
    TrackedObject(const std::string& name, size_t size)
        : name_(name), size_(size) {
        data_ = new int[size];
        g_alloc_count++;
        g_total_bytes += size * sizeof(int);
        std::cout << "  [分配] " << name_ << ": " << size * sizeof(int) << " 字节\n";
    }

    ~TrackedObject() {
        delete[] data_;
        g_free_count++;
        std::cout << "  [释放] " << name_ << "\n";
    }

    // 移动构造
    TrackedObject(TrackedObject&& other) noexcept
        : name_(std::move(other.name_)), data_(other.data_), size_(other.size_) {
        other.data_ = nullptr;
        other.size_ = 0;
        std::cout << "  [移动] " << name_ << " (零拷贝转移所有权)\n";
    }

    // 禁止拷贝
    TrackedObject(const TrackedObject&) = delete;

    const std::string& name() const { return name_; }
    size_t size() const { return size_; }
};

// ---- 3. 完美转发 ----
template<typename T, typename... Args>
std::unique_ptr<T> make_tracked(Args&&... args) {
    return std::make_unique<T>(std::forward<Args>(args)...);
}

// ---- 4. 循环引用演示 ----
class Node {
public:
    std::string name;
    std::shared_ptr<Node> next;      // 强引用
    std::weak_ptr<Node> weak_next;   // 弱引用(打破循环)

    Node(const std::string& n) : name(n) {
        std::cout << "  [Node 构造] " << name << "\n";
    }
    ~Node() {
        std::cout << "  [Node 析构] " << name << "\n";
    }
};

void run_memory() {
    g_alloc_count = 0;
    g_free_count = 0;
    g_total_bytes = 0;

    // ---- RAII ----
    std::cout << "\n--- RAII (资源获取即初始化) ---\n";
    {
        FileHandle f1("config.ini");
        f1.write("key=value");

        // 移动所有权
        FileHandle f2 = std::move(f1);
        f2.write("another=data");

        std::cout << "  作用域即将结束...\n";
    }
    std::cout << "  (RAII确保资源在作用域结束时自动释放)\n";

    // ---- unique_ptr ----
    std::cout << "\n--- unique_ptr (独占所有权) ---\n";
    {
        auto p1 = std::make_unique<TrackedObject>("传感器数据", 1000);
        std::cout << "  p1 持有: " << p1->name() << " (" << p1->size() << "元素)\n";

        // 转移所有权
        auto p2 = std::move(p1);
        std::cout << "  p1 有效? " << (p1 ? "是" : "否") << "\n";
        std::cout << "  p2 持有: " << p2->name() << "\n";

        // 完美转发 + make_unique
        auto p3 = make_tracked<TrackedObject>("图像缓冲区", 2048);
        std::cout << "  p3 持有: " << p3->name() << "\n";
    }
    std::cout << "  (离开作用域，unique_ptr 自动释放内存)\n";

    // ---- shared_ptr ----
    std::cout << "\n--- shared_ptr (共享所有权) ---\n";
    {
        auto sp1 = std::make_shared<TrackedObject>("共享配置", 100);
        std::cout << "  引用计数: " << sp1.use_count() << "\n";
        {
            auto sp2 = sp1;
            std::cout << "  拷贝后引用计数: " << sp1.use_count() << "\n";
            auto sp3 = sp1;
            std::cout << "  再拷贝: " << sp1.use_count() << "\n";
        }
        std::cout << "  sp2/sp3 销毁后: " << sp1.use_count() << "\n";
    }
    std::cout << "  (最后一个 shared_ptr 销毁时释放)\n";

    // ---- weak_ptr 打破循环引用 ----
    std::cout << "\n--- weak_ptr (打破循环引用) ---\n";
    std::cout << "  场景: A→B→A 的循环引用\n";
    {
        auto a = std::make_shared<Node>("A");
        auto b = std::make_shared<Node>("B");

        // 用 weak_ptr 代替 shared_ptr 打破循环
        a->weak_next = b;
        b->weak_next = a;

        std::cout << "  A引用计数: " << a.use_count() << "\n";
        std::cout << "  B引用计数: " << b.use_count() << "\n";

        // 使用 weak_ptr
        if (auto locked = a->weak_next.lock()) {
            std::cout << "  A的下一个节点: " << locked->name << "\n";
        }

        std::cout << "  (weak_ptr 不增加引用计数，避免内存泄漏)\n";
    }

    // ---- 移动语义的性能优势 ----
    std::cout << "\n--- 移动语义性能演示 ---\n";
    {
        std::vector<std::string> vec;
        std::string large_str(100, 'X');  // 100字符的字符串

        // 拷贝：复制整个字符串内容
        vec.push_back(large_str);
        std::cout << "  push_back(拷贝): large_str仍然有效, 长度=" << large_str.size() << "\n";

        // 移动：直接转移内部指针，零拷贝
        vec.push_back(std::move(large_str));
        std::cout << "  push_back(移动): large_str被掏空, 长度=" << large_str.size() << "\n";
        std::cout << "  (移动语义避免了不必要的内存拷贝)\n";
    }

    // ---- 内存统计 ----
    std::cout << "\n--- 内存统计 ---\n";
    std::cout << "  总分配次数: " << g_alloc_count << "\n";
    std::cout << "  总释放次数: " << g_free_count << "\n";
    std::cout << "  总分配字节: " << g_total_bytes << "\n";
    std::cout << "  泄漏检测: " << (g_alloc_count == g_free_count ? "无泄漏" : "有泄漏!") << "\n";

    std::cout << "\n[内存管理模块完成]\n";
}
