// ============================================================
// 07_concurrency.cpp — 并发编程
// 覆盖：std::thread、mutex、lock_guard、condition_variable、
//       future/async、atomic、简易线程池
// ============================================================

#include <iostream>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <future>
#include <atomic>
#include <vector>
#include <queue>
#include <functional>
#include <chrono>
#include <numeric>
#include <string>

// ---- 1. 基础多线程 ----
void worker(int id, int count) {
    for (int i = 0; i < count; ++i) {
        // 模拟工作
    }
    std::cout << "  线程" << id << " 完成 " << count << " 次迭代\n";
}

// ---- 2. 互斥锁保护共享数据 ----
class ThreadSafeCounter {
    int value_ = 0;
    mutable std::mutex mutex_;

public:
    void increment() {
        std::lock_guard<std::mutex> lock(mutex_);
        ++value_;
    }

    int get() const {
        std::lock_guard<std::mutex> lock(mutex_);
        return value_;
    }
};

// ---- 3. 生产者-消费者(condition_variable) ----
class ProducerConsumer {
    std::queue<int> queue_;
    std::mutex mutex_;
    std::condition_variable cv_;
    bool done_ = false;

public:
    void produce(int count) {
        for (int i = 0; i < count; ++i) {
            {
                std::lock_guard<std::mutex> lock(mutex_);
                queue_.push(i);
            }
            cv_.notify_one();
        }
        {
            std::lock_guard<std::mutex> lock(mutex_);
            done_ = true;
        }
        cv_.notify_all();
    }

    void consume(int id) {
        while (true) {
            std::unique_lock<std::mutex> lock(mutex_);
            cv_.wait(lock, [this] { return !queue_.empty() || done_; });

            if (queue_.empty() && done_) break;

            int val = queue_.front();
            queue_.pop();
            lock.unlock();

            // 不在输出中包含 val 的具体值以减少交错输出
            (void)val;
        }
        std::cout << "  消费者" << id << " 完成\n";
    }
};

// ---- 4. 简易线程池 ----
class ThreadPool {
    std::vector<std::thread> workers_;
    std::queue<std::function<void()>> tasks_;
    std::mutex mutex_;
    std::condition_variable cv_;
    bool stop_ = false;

public:
    ThreadPool(size_t threads) {
        for (size_t i = 0; i < threads; ++i) {
            workers_.emplace_back([this] {
                while (true) {
                    std::function<void()> task;
                    {
                        std::unique_lock<std::mutex> lock(mutex_);
                        cv_.wait(lock, [this] { return stop_ || !tasks_.empty(); });
                        if (stop_ && tasks_.empty()) return;
                        task = std::move(tasks_.front());
                        tasks_.pop();
                    }
                    task();
                }
            });
        }
    }

    void submit(std::function<void()> task) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            tasks_.push(std::move(task));
        }
        cv_.notify_one();
    }

    ~ThreadPool() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_ = true;
        }
        cv_.notify_all();
        for (auto& w : workers_) w.join();
    }
};

void run_concurrency() {
    // ---- 基础多线程 ----
    std::cout << "\n--- 基础多线程 ---\n";
    std::cout << "硬件并发数: " << std::thread::hardware_concurrency() << "\n";
    {
        std::vector<std::thread> threads;
        for (int i = 0; i < 4; ++i) {
            threads.emplace_back(worker, i, 1000000);
        }
        for (auto& t : threads) t.join();
        std::cout << "  所有线程完成\n";
    }

    // ---- 互斥锁 ----
    std::cout << "\n--- 互斥锁(线程安全计数器) ---\n";
    {
        ThreadSafeCounter counter;
        std::vector<std::thread> threads;
        for (int i = 0; i < 8; ++i) {
            threads.emplace_back([&counter]() {
                for (int j = 0; j < 10000; ++j) {
                    counter.increment();
                }
            });
        }
        for (auto& t : threads) t.join();
        std::cout << "  8个线程各自增10000次, 最终值: " << counter.get() << "\n";
        std::cout << "  (期望值: 80000, " << (counter.get() == 80000 ? "正确!" : "错误!") << ")\n";
    }

    // ---- atomic ----
    std::cout << "\n--- atomic (无锁原子操作) ---\n";
    {
        std::atomic<int> counter{0};
        std::vector<std::thread> threads;
        for (int i = 0; i < 8; ++i) {
            threads.emplace_back([&counter]() {
                for (int j = 0; j < 10000; ++j) {
                    counter.fetch_add(1, std::memory_order_relaxed);
                }
            });
        }
        for (auto& t : threads) t.join();
        std::cout << "  atomic 计数: " << counter.load() << " (期望80000)\n";
        std::cout << "  (无锁，比mutex更快)\n";
    }

    // ---- future/async ----
    std::cout << "\n--- future/async (异步计算) ---\n";
    {
        // 异步计算：并行求和
        auto async_sum = [](int start, int end) {
            long long sum = 0;
            for (int i = start; i < end; ++i) sum += i;
            return sum;
        };

        auto f1 = std::async(std::launch::async, async_sum, 0, 5000000);
        auto f2 = std::async(std::launch::async, async_sum, 5000000, 10000000);

        long long result = f1.get() + f2.get();
        std::cout << "  并行求和 [0, 10000000): " << result << "\n";

        // 验证
        long long expected = (long long)10000000 * 9999999 / 2;
        std::cout << "  验证: " << (result == expected ? "正确" : "错误") << "\n";
    }

    // ---- 生产者-消费者 ----
    std::cout << "\n--- 生产者-消费者 ---\n";
    {
        ProducerConsumer pc;
        std::thread producer([&pc]() { pc.produce(100); });
        std::thread consumer1([&pc]() { pc.consume(1); });
        std::thread consumer2([&pc]() { pc.consume(2); });

        producer.join();
        consumer1.join();
        consumer2.join();
        std::cout << "  生产100个任务, 2个消费者完成处理\n";
    }

    // ---- 线程池 ----
    std::cout << "\n--- 线程池 ---\n";
    {
        std::atomic<int> completed{0};
        {
            ThreadPool pool(4);
            for (int i = 0; i < 20; ++i) {
                pool.submit([&completed]() {
                    // 模拟工作
                    std::this_thread::sleep_for(std::chrono::milliseconds(1));
                    completed.fetch_add(1);
                });
            }
        }
        std::cout << "  4线程池执行20个任务, 完成: " << completed.load() << "\n";
    }

    std::cout << "\n[并发编程模块完成]\n";
}
