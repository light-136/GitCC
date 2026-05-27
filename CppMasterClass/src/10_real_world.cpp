// ============================================================
// 10_real_world.cpp — 实战：模拟运动控制器
// 与 SmartSemiCon 概念呼应，用纯 C++ 实现：
//   轴类、梯形速度规划、状态机、多线程调度
// ============================================================

#include <iostream>
#include <string>
#include <vector>
#include <thread>
#include <mutex>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iomanip>
#include <functional>
#include <map>

// ---- 轴状态枚举 ----
enum class AxisState {
    Idle,       // 空闲
    Moving,     // 运动中
    Homing,     // 回原中
    Error       // 故障
};

const char* state_name(AxisState s) {
    switch (s) {
        case AxisState::Idle:   return "空闲";
        case AxisState::Moving: return "运动中";
        case AxisState::Homing: return "回原中";
        case AxisState::Error:  return "故障";
        default: return "未知";
    }
}

// ---- 轴类 — 模拟真实运动控制轴 ----
class SimAxis {
    int id_;
    std::string name_;
    double position_ = 0;
    double target_ = 0;
    double velocity_ = 0;
    double max_speed_;
    double acceleration_;
    AxisState state_ = AxisState::Idle;
    bool servo_on_ = false;
    mutable std::mutex mutex_;

public:
    SimAxis(int id, const std::string& name, double max_speed, double accel)
        : id_(id), name_(name), max_speed_(max_speed), acceleration_(accel) {}

    void servoOn() {
        std::lock_guard<std::mutex> lock(mutex_);
        servo_on_ = true;
    }

    // 梯形速度规划运动模拟
    bool moveAbsolute(double target, double speed) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!servo_on_) return false;
            target_ = target;
            state_ = AxisState::Moving;
        }

        double actual_speed = std::min(speed, max_speed_);
        double direction = (target > position_) ? 1.0 : -1.0;

        // 简化的梯形规划模拟
        double dt = 0.01;  // 10ms 更新周期
        double current_vel = 0;

        while (true) {
            double remaining = std::abs(target - position_);
            if (remaining < 0.001) break;

            // 减速距离
            double decel_dist = current_vel * current_vel / (2 * acceleration_);

            if (remaining <= decel_dist) {
                // 减速阶段
                current_vel -= acceleration_ * dt;
                if (current_vel < 0.1) current_vel = 0.1;
            } else if (current_vel < actual_speed) {
                // 加速阶段
                current_vel += acceleration_ * dt;
                if (current_vel > actual_speed) current_vel = actual_speed;
            }

            {
                std::lock_guard<std::mutex> lock(mutex_);
                position_ += direction * current_vel * dt;
                velocity_ = current_vel * direction;
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        {
            std::lock_guard<std::mutex> lock(mutex_);
            position_ = target;
            velocity_ = 0;
            state_ = AxisState::Idle;
        }
        return true;
    }

    void home() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            state_ = AxisState::Homing;
        }
        moveAbsolute(0, max_speed_ * 0.5);
        {
            std::lock_guard<std::mutex> lock(mutex_);
            state_ = AxisState::Idle;
        }
    }

    // 获取状态快照
    void printStatus() const {
        std::lock_guard<std::mutex> lock(mutex_);
        std::cout << "  轴" << id_ << "[" << name_ << "] "
                  << "位置=" << std::fixed << std::setprecision(3) << position_ << "mm "
                  << "速度=" << std::setprecision(1) << velocity_ << "mm/s "
                  << "状态=" << state_name(state_) << " "
                  << "使能=" << (servo_on_ ? "ON" : "OFF") << "\n";
    }

    double position() const { std::lock_guard<std::mutex> lock(mutex_); return position_; }
    AxisState state() const { std::lock_guard<std::mutex> lock(mutex_); return state_; }
};

// ---- 设备状态机 ----
class DeviceFSM {
    enum class State { Idle, Init, Running, Alarm, EStop };

    State state_ = State::Idle;
    std::map<std::string, std::function<bool()>> transitions_;
    std::mutex mutex_;

    const char* state_str(State s) {
        switch (s) {
            case State::Idle:    return "空闲";
            case State::Init:    return "初始化";
            case State::Running: return "运行中";
            case State::Alarm:   return "报警";
            case State::EStop:   return "急停";
            default: return "?";
        }
    }

public:
    DeviceFSM() {
        transitions_["initialize"] = [this]() {
            if (state_ != State::Idle) return false;
            state_ = State::Init;
            return true;
        };
        transitions_["start"] = [this]() {
            if (state_ != State::Init) return false;
            state_ = State::Running;
            return true;
        };
        transitions_["stop"] = [this]() {
            state_ = State::Idle;
            return true;
        };
        transitions_["alarm"] = [this]() {
            state_ = State::Alarm;
            return true;
        };
        transitions_["reset"] = [this]() {
            if (state_ == State::Alarm) { state_ = State::Idle; return true; }
            return false;
        };
    }

    bool fire(const std::string& trigger) {
        std::lock_guard<std::mutex> lock(mutex_);
        auto it = transitions_.find(trigger);
        if (it == transitions_.end()) return false;
        auto old = state_;
        bool ok = it->second();
        if (ok) {
            std::cout << "  [状态机] " << state_str(old) << " -> " << state_str(state_)
                      << " (触发: " << trigger << ")\n";
        }
        return ok;
    }
};

void run_real_world() {
    std::cout << "\n--- 模拟运动控制器 (半导体设备) ---\n";
    std::cout << "  (与 SmartSemiCon 的 SimulationAxisController 概念相同)\n\n";

    // 创建轴
    std::cout << "== 创建运动轴 ==\n";
    SimAxis axisX(0, "X轴-搬运", 200, 800);
    SimAxis axisY(1, "Y轴-搬运", 200, 800);
    SimAxis axisZ(2, "Z轴-升降", 100, 500);

    // 创建状态机
    DeviceFSM fsm;

    // 初始化
    std::cout << "\n== 初始化流程 ==\n";
    fsm.fire("initialize");
    axisX.servoOn();
    axisY.servoOn();
    axisZ.servoOn();
    std::cout << "  所有轴使能完成\n";

    // 回原
    std::cout << "\n== 多轴并行回原 ==\n";
    {
        std::thread t1([&]() { axisX.home(); });
        std::thread t2([&]() { axisY.home(); });
        std::thread t3([&]() { axisZ.home(); });
        t1.join(); t2.join(); t3.join();
    }
    std::cout << "  回原完成\n";
    axisX.printStatus();
    axisY.printStatus();
    axisZ.printStatus();

    // 启动自动运行
    fsm.fire("start");

    // 模拟一个简单的搬运流程
    std::cout << "\n== 搬运流程模拟 ==\n";

    // 步骤1：移动到取料位
    std::cout << "\n  [步骤1] 移动到取料位 (100, 50)\n";
    {
        std::thread tx([&]() { axisX.moveAbsolute(100, 80); });
        std::thread ty([&]() { axisY.moveAbsolute(50, 80); });
        tx.join(); ty.join();
    }
    axisX.printStatus();
    axisY.printStatus();

    // 步骤2：Z轴下降取料
    std::cout << "\n  [步骤2] Z轴下降取料\n";
    axisZ.moveAbsolute(-30, 50);
    axisZ.printStatus();

    // 步骤3：Z轴上升
    std::cout << "\n  [步骤3] Z轴上升\n";
    axisZ.moveAbsolute(0, 50);

    // 步骤4：搬运到放料位
    std::cout << "\n  [步骤4] 搬运到放料位 (200, 100)\n";
    {
        std::thread tx([&]() { axisX.moveAbsolute(200, 100); });
        std::thread ty([&]() { axisY.moveAbsolute(100, 100); });
        tx.join(); ty.join();
    }
    axisX.printStatus();
    axisY.printStatus();

    // 步骤5：Z轴下降放料
    std::cout << "\n  [步骤5] Z轴下降放料\n";
    axisZ.moveAbsolute(-30, 50);
    axisZ.moveAbsolute(0, 50);

    // 步骤6：回到待机位
    std::cout << "\n  [步骤6] 回到待机位\n";
    {
        std::thread tx([&]() { axisX.moveAbsolute(0, 100); });
        std::thread ty([&]() { axisY.moveAbsolute(0, 100); });
        std::thread tz([&]() { axisZ.moveAbsolute(0, 50); });
        tx.join(); ty.join(); tz.join();
    }

    // 最终状态
    std::cout << "\n== 最终状态 ==\n";
    axisX.printStatus();
    axisY.printStatus();
    axisZ.printStatus();

    fsm.fire("stop");

    std::cout << "\n  搬运流程完成！\n";
    std::cout << "  (本模块演示了C++在工业控制中的应用：\n";
    std::cout << "   多线程并行控制、互斥锁同步、状态机、\n";
    std::cout << "   梯形速度规划 — 与 SmartSemiCon 的设计理念一致)\n";

    std::cout << "\n[实战模块完成]\n";
}
