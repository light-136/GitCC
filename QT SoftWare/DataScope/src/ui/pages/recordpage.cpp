/**
 * @file recordpage.cpp
 * @brief 数据记录与回放页实现（P15）
 *
 * UI 结构（两个功能区，QGroupBox 分组）：
 *   ┌ 实时数据记录 ─────────────────────────┐
 *   │ [开始记录] [停止记录]   状态: 未记录      │
 *   └────────────────────────────────────────┘
 *   ┌ 历史回放 ─────────────────────────────┐
 *   │ [加载文件] [开始回放] [停止回放]         │
 *   │ 文件: 未加载     进度: ▓▓▓▓░░ 3/10     │
 *   └────────────────────────────────────────┘
 *
 * 按钮可用性随状态切换（用户只被允许点"当前合理"的操作）：
 *   - 记录：开始(可)→记录中(开始禁用/停止启用)→停止
 *   - 回放：加载→可开始；回放中→停止可用；播完→复位
 */

#include "ui/pages/recordpage.h"

#include "services/recordmanager.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QLabel>
#include <QPushButton>
#include <QProgressBar>
#include <QFileDialog>
#include <QMessageBox>
#include <QDateTime>
#include <QDebug>

namespace datascope {
namespace ui {

RecordPage::RecordPage(QWidget *parent)
    : QWidget(parent)
{
    setObjectName(QStringLiteral("recordPage"));   // QSS 选择器定位
    buildUi();
}

void RecordPage::setRecordManager(datascope::services::RecordManager *manager)
{
    m_manager = manager;
    // 回放进度与结束由管理器信号驱动 UI（进度条/按钮复位）
    connect(m_manager, &datascope::services::RecordManager::progressChanged,
            this, &RecordPage::onReplayProgress);
    connect(m_manager, &datascope::services::RecordManager::replayFinished,
            this, &RecordPage::onReplayFinished);
    connect(m_manager, &datascope::services::RecordManager::errorOccurred,
            this, [this](const QString &msg) {
        QMessageBox::warning(this, tr("记录/回放"), msg);
    });
}

void RecordPage::buildUi()
{
    auto *layout = new QVBoxLayout(this);

    auto *titleLabel = new QLabel(tr("数据记录与回放"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ================= ① 实时数据记录 =================
    auto *recordBox = new QGroupBox(tr("实时数据记录"), this);
    auto *recordLayout = new QHBoxLayout(recordBox);

    m_startRecordBtn = new QPushButton(tr("开始记录"), recordBox);
    m_stopRecordBtn  = new QPushButton(tr("停止记录"), recordBox);
    m_stopRecordBtn->setEnabled(false);   // 未记录时不可停止
    m_recordStateLabel = new QLabel(tr("状态：未记录"), recordBox);

    connect(m_startRecordBtn, &QPushButton::clicked, this, &RecordPage::onStartRecordClicked);
    connect(m_stopRecordBtn,  &QPushButton::clicked, this, &RecordPage::onStopRecordClicked);

    recordLayout->addWidget(m_startRecordBtn);
    recordLayout->addWidget(m_stopRecordBtn);
    recordLayout->addWidget(m_recordStateLabel, 1);

    // ================= ② 历史回放 =================
    auto *replayBox = new QGroupBox(tr("历史回放"), this);
    auto *replayLayout = new QVBoxLayout(replayBox);

    auto *btnRow = new QHBoxLayout;
    m_loadReplayBtn  = new QPushButton(tr("加载回放文件..."), replayBox);
    m_startReplayBtn = new QPushButton(tr("开始回放"), replayBox);
    m_stopReplayBtn  = new QPushButton(tr("停止回放"), replayBox);
    m_startReplayBtn->setEnabled(false);   // 未加载文件不可回放
    m_stopReplayBtn->setEnabled(false);    // 未回放不可停止

    connect(m_loadReplayBtn,  &QPushButton::clicked, this, &RecordPage::onLoadReplayClicked);
    connect(m_startReplayBtn, &QPushButton::clicked, this, &RecordPage::onStartReplayClicked);
    connect(m_stopReplayBtn,  &QPushButton::clicked, this, &RecordPage::onStopReplayClicked);

    btnRow->addWidget(m_loadReplayBtn);
    btnRow->addWidget(m_startReplayBtn);
    btnRow->addWidget(m_stopReplayBtn);
    btnRow->addStretch(1);

    m_replayFileLabel = new QLabel(tr("文件：未加载"), replayBox);
    m_replayProgress  = new QProgressBar(replayBox);
    m_replayProgress->setRange(0, 1);     // 初始 0~1：未加载时静止
    m_replayProgress->setValue(0);
    m_replayProgress->setFormat(tr("%v / %m 帧"));

    replayLayout->addLayout(btnRow);
    replayLayout->addWidget(m_replayFileLabel);
    replayLayout->addWidget(m_replayProgress);

    // 组装
    layout->addWidget(titleLabel);
    layout->addWidget(recordBox);
    layout->addWidget(replayBox);
    layout->addStretch(1);
}

// ---------------------------------------------------------------------------
// 记录
// ---------------------------------------------------------------------------
void RecordPage::onStartRecordClicked()
{
    if (!m_manager) {
        return;
    }
    // 选择保存路径（默认带时间戳文件名，便于归档）
    const QString suggested = QStringLiteral("record_%1.csv")
        .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_HHmmss")));
    const QString path = QFileDialog::getSaveFileName(
        this, tr("保存记录文件"), suggested, tr("CSV 文件 (*.csv)"));
    if (path.isEmpty()) {
        return;   // 用户取消
    }
    if (m_manager->startRecording(path)) {
        m_recordStateLabel->setText(tr("状态：正在记录 → %1").arg(path));
        m_startRecordBtn->setEnabled(false);
        m_stopRecordBtn->setEnabled(true);
    }
    // 失败时 startRecording 内部已发 errorOccurred → 弹窗提示
}

void RecordPage::onStopRecordClicked()
{
    if (!m_manager) {
        return;
    }
    m_manager->stopRecording();
    m_recordStateLabel->setText(tr("状态：已停止记录"));
    m_startRecordBtn->setEnabled(true);
    m_stopRecordBtn->setEnabled(false);
}

// ---------------------------------------------------------------------------
// 回放
// ---------------------------------------------------------------------------
void RecordPage::onLoadReplayClicked()
{
    if (!m_manager) {
        return;
    }
    const QString path = QFileDialog::getOpenFileName(
        this, tr("选择回放文件"), QString(), tr("CSV 文件 (*.csv)"));
    if (path.isEmpty()) {
        return;
    }
    if (m_manager->loadReplay(path)) {
        const int frameCount = m_manager->replayFrameCount();
        m_replayFileLabel->setText(tr("文件：%1（%2 帧）").arg(path).arg(frameCount));
        m_replayProgress->setRange(0, frameCount);
        m_replayProgress->setValue(0);
        m_startReplayBtn->setEnabled(frameCount > 0);   // 空文件禁回放
    }
}

void RecordPage::onStartReplayClicked()
{
    if (!m_manager) {
        return;
    }
    m_manager->startReplay();
    m_startReplayBtn->setEnabled(false);
    m_stopReplayBtn->setEnabled(true);
}

void RecordPage::onStopReplayClicked()
{
    if (!m_manager) {
        return;
    }
    m_manager->stopReplay();
    m_startReplayBtn->setEnabled(true);
    m_stopReplayBtn->setEnabled(false);
}

void RecordPage::onReplayProgress(int current)
{
    m_replayProgress->setValue(current);   // 管理器每发一帧就刷新一次
}

void RecordPage::onReplayFinished()
{
    m_replayProgress->setValue(m_replayProgress->maximum());   // 播完置满
    m_startReplayBtn->setEnabled(true);
    m_stopReplayBtn->setEnabled(false);
}

} // namespace ui
} // namespace datascope
