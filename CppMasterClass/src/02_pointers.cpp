// ============================================================
// 02_pointers.cpp — 指针与引用
// 覆盖：原始指针、new/delete、指针算术、void*、
//       引用、const引用、智能指针概述(详见06_memory)
// ============================================================

#include <iostream>
#include <memory>
#include <string>

void run_pointers() {
    // ---- 1. 原始指针基础 ----
    std::cout << "\n--- 原始指针基础 ---\n";
    int x = 42;
    int* p = &x;  // p 存储 x 的地址
    std::cout << "x 的值:  " << x << "\n";
    std::cout << "x 的地址: " << &x << "\n";
    std::cout << "p 的值(地址): " << p << "\n";
    std::cout << "*p (解引用): " << *p << "\n";

    *p = 100;  // 通过指针修改
    std::cout << "修改后 x = " << x << "\n";

    // ---- 2. 指针算术 ----
    std::cout << "\n--- 指针算术 ---\n";
    int arr[] = {10, 20, 30, 40, 50};
    int* pa = arr;  // 数组名是首元素指针
    std::cout << "arr[0] = " << *pa << "\n";
    std::cout << "arr[1] = " << *(pa + 1) << "\n";
    std::cout << "arr[2] = " << *(pa + 2) << "\n";
    std::cout << "指针步进量 = " << sizeof(int) << " 字节\n";

    // ---- 3. new / delete ----
    std::cout << "\n--- 动态内存 (new/delete) ---\n";
    int* heap_int = new int(999);
    std::cout << "堆上分配: *heap_int = " << *heap_int << "\n";
    delete heap_int;
    // 注意：delete 后指针变成悬空指针，不能再使用

    int* heap_arr = new int[5]{1, 2, 3, 4, 5};
    std::cout << "堆上数组: ";
    for (int i = 0; i < 5; ++i) std::cout << heap_arr[i] << " ";
    std::cout << "\n";
    delete[] heap_arr;

    // ---- 4. void* — 通用指针 ----
    std::cout << "\n--- void* (通用指针) ---\n";
    int val = 77;
    void* vp = &val;
    // void* 不能直接解引用，必须先转换
    std::cout << "void* 转 int*: " << *static_cast<int*>(vp) << "\n";

    // ---- 5. 引用 ----
    std::cout << "\n--- 引用 ---\n";
    int a = 10;
    int& ref = a;  // ref 是 a 的别名
    std::cout << "a = " << a << ", ref = " << ref << "\n";
    ref = 20;
    std::cout << "修改ref后: a = " << a << "\n";
    std::cout << "地址相同: &a=" << &a << " &ref=" << &ref << "\n";

    // const 引用 — 不能通过引用修改
    const int& cref = a;
    std::cout << "const引用: cref = " << cref << " (不可修改)\n";

    // 引用作为函数参数(交换两个值)
    auto swap = [](int& x, int& y) {
        int temp = x;
        x = y;
        y = temp;
    };
    int m = 5, n = 8;
    std::cout << "交换前: m=" << m << " n=" << n << "\n";
    swap(m, n);
    std::cout << "交换后: m=" << m << " n=" << n << "\n";

    // ---- 6. nullptr (C++11) ----
    std::cout << "\n--- nullptr ---\n";
    int* np = nullptr;
    if (np == nullptr) {
        std::cout << "指针为空 (nullptr)\n";
    }
    // 与 NULL(0) 不同，nullptr 有独立类型 std::nullptr_t
    std::cout << "sizeof(nullptr) = " << sizeof(nullptr) << "\n";

    // ---- 7. 智能指针概述 ----
    std::cout << "\n--- 智能指针概述 ---\n";
    // unique_ptr — 独占所有权
    auto up = std::make_unique<int>(42);
    std::cout << "unique_ptr: " << *up << "\n";
    // auto up2 = up;  // 编译错误！不能拷贝
    auto up2 = std::move(up);  // 可以移动
    std::cout << "移动后: up2=" << *up2 << ", up=" << (up ? "有效" : "空") << "\n";

    // shared_ptr — 共享所有权
    auto sp1 = std::make_shared<std::string>("共享数据");
    auto sp2 = sp1;  // 引用计数+1
    std::cout << "shared_ptr 引用计数: " << sp1.use_count() << "\n";
    std::cout << "sp1=" << *sp1 << ", sp2=" << *sp2 << "\n";

    // weak_ptr — 不增加引用计数
    std::weak_ptr<std::string> wp = sp1;
    std::cout << "weak_ptr 引用计数: " << wp.use_count() << " (不增加)\n";
    if (auto locked = wp.lock()) {
        std::cout << "weak_ptr 锁定成功: " << *locked << "\n";
    }

    std::cout << "\n[指针与引用模块完成]\n";
}
