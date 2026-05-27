// ============================================================
// 03_oop.cpp — 面向对象编程
// 覆盖：类、构造/析构、拷贝/移动、继承、虚函数、
//       抽象类、运算符重载、友元、多态
// ============================================================

#include <iostream>
#include <string>
#include <vector>
#include <memory>
#include <cmath>

// ---- 1. 基础类：二维向量 ----
class Vector2D {
private:
    double x_, y_;

public:
    // 默认构造
    Vector2D() : x_(0), y_(0) {
        std::cout << "  [构造] Vector2D(0,0)\n";
    }

    // 参数化构造
    Vector2D(double x, double y) : x_(x), y_(y) {
        std::cout << "  [构造] Vector2D(" << x_ << "," << y_ << ")\n";
    }

    // 拷贝构造
    Vector2D(const Vector2D& other) : x_(other.x_), y_(other.y_) {
        std::cout << "  [拷贝构造] Vector2D(" << x_ << "," << y_ << ")\n";
    }

    // 移动构造(C++11)
    Vector2D(Vector2D&& other) noexcept : x_(other.x_), y_(other.y_) {
        other.x_ = 0;
        other.y_ = 0;
        std::cout << "  [移动构造] Vector2D(" << x_ << "," << y_ << ")\n";
    }

    // 析构
    ~Vector2D() {
        std::cout << "  [析构] Vector2D(" << x_ << "," << y_ << ")\n";
    }

    // 运算符重载: +
    Vector2D operator+(const Vector2D& rhs) const {
        return Vector2D(x_ + rhs.x_, y_ + rhs.y_);
    }

    // 运算符重载: ==
    bool operator==(const Vector2D& rhs) const {
        return x_ == rhs.x_ && y_ == rhs.y_;
    }

    // 友元函数: 允许外部访问私有成员
    friend std::ostream& operator<<(std::ostream& os, const Vector2D& v);

    double length() const { return std::sqrt(x_ * x_ + y_ * y_); }
    double getX() const { return x_; }
    double getY() const { return y_; }
};

// 友元实现
std::ostream& operator<<(std::ostream& os, const Vector2D& v) {
    os << "(" << v.x_ << ", " << v.y_ << ")";
    return os;
}

// ---- 2. 继承与多态 ----

// 抽象基类: 图形
class Shape {
protected:
    std::string name_;

public:
    Shape(const std::string& name) : name_(name) {}
    virtual ~Shape() = default;

    // 纯虚函数 — 子类必须实现
    virtual double area() const = 0;
    virtual double perimeter() const = 0;

    // 虚函数 — 子类可以覆盖
    virtual void describe() const {
        std::cout << "  图形[" << name_ << "] 面积=" << area() << " 周长=" << perimeter() << "\n";
    }

    const std::string& name() const { return name_; }
};

// 派生类: 圆形
class Circle : public Shape {
    double radius_;
public:
    Circle(double r) : Shape("圆形"), radius_(r) {}

    double area() const override { return 3.14159 * radius_ * radius_; }
    double perimeter() const override { return 2 * 3.14159 * radius_; }
};

// 派生类: 矩形
class Rectangle : public Shape {
    double width_, height_;
public:
    Rectangle(double w, double h) : Shape("矩形"), width_(w), height_(h) {}

    double area() const override { return width_ * height_; }
    double perimeter() const override { return 2 * (width_ + height_); }
};

// 派生类: 三角形
class Triangle : public Shape {
    double a_, b_, c_;
public:
    Triangle(double a, double b, double c)
        : Shape("三角形"), a_(a), b_(b), c_(c) {}

    double area() const override {
        double s = (a_ + b_ + c_) / 2;
        return std::sqrt(s * (s - a_) * (s - b_) * (s - c_));
    }
    double perimeter() const override { return a_ + b_ + c_; }
};

// ---- 3. 多重继承 ----
class Printable {
public:
    virtual void print() const = 0;
    virtual ~Printable() = default;
};

class Serializable {
public:
    virtual std::string serialize() const = 0;
    virtual ~Serializable() = default;
};

class DataPoint : public Printable, public Serializable {
    double x_, y_;
    std::string label_;
public:
    DataPoint(double x, double y, const std::string& label)
        : x_(x), y_(y), label_(label) {}

    void print() const override {
        std::cout << "  DataPoint[" << label_ << "] = (" << x_ << ", " << y_ << ")\n";
    }

    std::string serialize() const override {
        return label_ + ":" + std::to_string(x_) + "," + std::to_string(y_);
    }
};

void run_oop() {
    // ---- 构造与析构的生命周期 ----
    std::cout << "\n--- 对象生命周期 ---\n";
    {
        Vector2D v1(3, 4);
        Vector2D v2(1, 2);
        Vector2D v3 = v1 + v2;  // 运算符重载
        std::cout << "v1=" << v1 << " v2=" << v2 << " v1+v2=" << v3 << "\n";
        std::cout << "v1长度 = " << v1.length() << "\n";

        // 拷贝
        Vector2D v4 = v1;
        std::cout << "拷贝: v4=" << v4 << "\n";

        // 移动
        Vector2D v5 = std::move(v4);
        std::cout << "移动后: v5=" << v5 << " v4=" << v4 << "\n";
        std::cout << "--- 作用域结束，析构开始 ---\n";
    }

    // ---- 多态 ----
    std::cout << "\n--- 多态(虚函数) ---\n";
    std::vector<std::unique_ptr<Shape>> shapes;
    shapes.push_back(std::make_unique<Circle>(5.0));
    shapes.push_back(std::make_unique<Rectangle>(4.0, 6.0));
    shapes.push_back(std::make_unique<Triangle>(3.0, 4.0, 5.0));

    // 基类指针调用虚函数 — 运行时多态
    for (const auto& shape : shapes) {
        shape->describe();
    }

    // ---- 多重继承 ----
    std::cout << "\n--- 多重继承 ---\n";
    DataPoint dp(100.5, 200.3, "传感器A");
    dp.print();
    std::cout << "  序列化: " << dp.serialize() << "\n";

    // ---- 运算符 == 重载 ----
    std::cout << "\n--- 运算符重载 ---\n";
    Vector2D a(1, 1), b(1, 1), c(2, 2);
    std::cout << "a==b: " << (a == b ? "true" : "false") << "\n";
    std::cout << "a==c: " << (a == c ? "true" : "false") << "\n";

    std::cout << "\n[面向对象模块完成]\n";
}
