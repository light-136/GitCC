// ============================================================
// 09_design_patterns.cpp — 设计模式 (C++实现)
// 覆盖：工厂方法、单例、观察者、策略、CRTP
// ============================================================

#include <iostream>
#include <memory>
#include <string>
#include <vector>
#include <functional>
#include <map>
#include <mutex>
#include <cmath>

// ======== 1. 工厂方法模式 ========
// 根据类型名称创建不同的传感器对象
class Sensor {
public:
    virtual ~Sensor() = default;
    virtual std::string read() const = 0;
    virtual std::string type() const = 0;
};

class TemperatureSensor : public Sensor {
public:
    std::string read() const override { return "25.6 C"; }
    std::string type() const override { return "温度传感器"; }
};

class PressureSensor : public Sensor {
public:
    std::string read() const override { return "1013.25 hPa"; }
    std::string type() const override { return "压力传感器"; }
};

class ProximitySensor : public Sensor {
public:
    std::string read() const override { return "15.3 mm"; }
    std::string type() const override { return "接近传感器"; }
};

// 工厂函数
class SensorFactory {
public:
    static std::unique_ptr<Sensor> create(const std::string& type) {
        if (type == "temperature") return std::make_unique<TemperatureSensor>();
        if (type == "pressure")    return std::make_unique<PressureSensor>();
        if (type == "proximity")   return std::make_unique<ProximitySensor>();
        return nullptr;
    }
};

// ======== 2. 单例模式 (线程安全) ========
class SystemConfig {
    std::map<std::string, std::string> settings_;
    mutable std::mutex mutex_;

    SystemConfig() {
        settings_["version"] = "1.0.0";
        settings_["mode"] = "production";
        std::cout << "  [单例] SystemConfig 实例创建\n";
    }

public:
    // 禁止拷贝和移动
    SystemConfig(const SystemConfig&) = delete;
    SystemConfig& operator=(const SystemConfig&) = delete;

    static SystemConfig& instance() {
        static SystemConfig inst;  // C++11 保证线程安全
        return inst;
    }

    void set(const std::string& key, const std::string& value) {
        std::lock_guard<std::mutex> lock(mutex_);
        settings_[key] = value;
    }

    std::string get(const std::string& key) const {
        std::lock_guard<std::mutex> lock(mutex_);
        auto it = settings_.find(key);
        return it != settings_.end() ? it->second : "";
    }

    void dump() const {
        std::lock_guard<std::mutex> lock(mutex_);
        for (const auto& [k, v] : settings_) {
            std::cout << "    " << k << " = " << v << "\n";
        }
    }
};

// ======== 3. 观察者模式 ========
// 事件系统：发布-订阅
class EventBus {
    std::map<std::string, std::vector<std::function<void(const std::string&)>>> subscribers_;

public:
    void subscribe(const std::string& event, std::function<void(const std::string&)> handler) {
        subscribers_[event].push_back(std::move(handler));
    }

    void publish(const std::string& event, const std::string& data) {
        if (auto it = subscribers_.find(event); it != subscribers_.end()) {
            for (auto& handler : it->second) {
                handler(data);
            }
        }
    }
};

// ======== 4. 策略模式 ========
// 不同的运动控制策略
class MotionStrategy {
public:
    virtual ~MotionStrategy() = default;
    virtual std::string name() const = 0;
    virtual double calculate(double distance, double max_speed) const = 0;
};

class TrapezoidalStrategy : public MotionStrategy {
public:
    std::string name() const override { return "梯形加减速"; }
    double calculate(double distance, double max_speed) const override {
        // 简化计算：匀加速+匀速+匀减速
        double accel_time = max_speed / 500.0;  // 加速时间
        double accel_dist = 0.5 * 500.0 * accel_time * accel_time;
        if (2 * accel_dist >= distance) {
            return 2 * std::sqrt(distance / 500.0);  // 三角形
        }
        double cruise_dist = distance - 2 * accel_dist;
        return 2 * accel_time + cruise_dist / max_speed;
    }
};

class SCurveStrategy : public MotionStrategy {
public:
    std::string name() const override { return "S曲线加减速"; }
    double calculate(double distance, double max_speed) const override {
        // 简化：S曲线约比梯形慢15%但更平滑
        double trap_time = distance / max_speed;
        return trap_time * 1.15;
    }
};

class ConstantSpeedStrategy : public MotionStrategy {
public:
    std::string name() const override { return "匀速运动"; }
    double calculate(double distance, double max_speed) const override {
        return distance / max_speed;
    }
};

// 运动控制器使用策略
class MotionController {
    std::unique_ptr<MotionStrategy> strategy_;

public:
    void setStrategy(std::unique_ptr<MotionStrategy> strategy) {
        strategy_ = std::move(strategy);
    }

    void execute(double distance, double speed) {
        if (!strategy_) return;
        double time = strategy_->calculate(distance, speed);
        std::cout << "  策略[" << strategy_->name() << "]: "
                  << "距离=" << distance << "mm 速度=" << speed << "mm/s "
                  << "预计时间=" << time * 1000 << "ms\n";
    }
};

// ======== 5. CRTP (奇异递归模板模式) ========
// 静态多态 — 编译期决定行为，零虚函数开销
template<typename Derived>
class Loggable {
public:
    void log() const {
        // 编译期调用派生类方法
        std::cout << "  [LOG] " << static_cast<const Derived*>(this)->className()
                  << ": " << static_cast<const Derived*>(this)->info() << "\n";
    }
};

class AxisLog : public Loggable<AxisLog> {
    int id_;
    double pos_;
public:
    AxisLog(int id, double pos) : id_(id), pos_(pos) {}
    std::string className() const { return "Axis"; }
    std::string info() const { return "ID=" + std::to_string(id_) + " Pos=" + std::to_string(pos_); }
};

class AlarmLog : public Loggable<AlarmLog> {
    std::string msg_;
public:
    AlarmLog(const std::string& msg) : msg_(msg) {}
    std::string className() const { return "Alarm"; }
    std::string info() const { return msg_; }
};

void run_design_patterns() {
    // ---- 工厂方法 ----
    std::cout << "\n--- 工厂方法模式 ---\n";
    std::vector<std::string> sensor_types = {"temperature", "pressure", "proximity"};
    for (const auto& type : sensor_types) {
        auto sensor = SensorFactory::create(type);
        if (sensor) {
            std::cout << "  创建: " << sensor->type() << " -> 读数: " << sensor->read() << "\n";
        }
    }

    // ---- 单例 ----
    std::cout << "\n--- 单例模式 ---\n";
    auto& config1 = SystemConfig::instance();
    auto& config2 = SystemConfig::instance();
    std::cout << "  同一实例? " << (&config1 == &config2 ? "是" : "否") << "\n";
    config1.set("host", "192.168.1.100");
    config1.set("port", "5000");
    std::cout << "  配置内容:\n";
    config1.dump();

    // ---- 观察者 ----
    std::cout << "\n--- 观察者模式 ---\n";
    EventBus bus;
    bus.subscribe("alarm", [](const std::string& data) {
        std::cout << "  [报警处理器] 收到: " << data << "\n";
    });
    bus.subscribe("alarm", [](const std::string& data) {
        std::cout << "  [日志记录器] 记录报警: " << data << "\n";
    });
    bus.subscribe("state_change", [](const std::string& data) {
        std::cout << "  [状态监控] 状态变更: " << data << "\n";
    });

    bus.publish("alarm", "轴0正限位报警");
    bus.publish("state_change", "Idle -> Auto");

    // ---- 策略 ----
    std::cout << "\n--- 策略模式 ---\n";
    MotionController ctrl;

    ctrl.setStrategy(std::make_unique<TrapezoidalStrategy>());
    ctrl.execute(100, 50);

    ctrl.setStrategy(std::make_unique<SCurveStrategy>());
    ctrl.execute(100, 50);

    ctrl.setStrategy(std::make_unique<ConstantSpeedStrategy>());
    ctrl.execute(100, 50);

    // ---- CRTP ----
    std::cout << "\n--- CRTP (静态多态) ---\n";
    AxisLog axisLog(0, 123.456);
    AlarmLog alarmLog("温度超限 T=85.2C");
    axisLog.log();   // 编译期绑定，无虚函数开销
    alarmLog.log();
    std::cout << "  (CRTP实现零开销多态，比虚函数快)\n";

    std::cout << "\n[设计模式模块完成]\n";
}
