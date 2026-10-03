// LoginHistoryDlg.cpp : メインダイアログ

#include "stdafx.h"
#include "login_history.h"
#include "login_history_dlg.h"
#include "afxdialogex.h"

#include <share.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

namespace
{
	constexpr char kIniFileName[] = "set.ini";
	constexpr char kDefaultLogFileName[] = "C:\\Alive.log";
	constexpr char kIniKeyCycle[] = "Cycle";
	constexpr char kIniComment[] = ";";
	constexpr char kWarningMark[] = " !";		// 間隔がずれた記録の行末に付ける印

	constexpr int kDefaultCycleMinutes = 60;	// 記録間隔（分）
	constexpr int kAllowableLagSeconds = 10;	// 記録間隔のずれの許容量（秒）
	constexpr int kLineBufferSize = 30;			// 1行の読み込みバッファ
	constexpr int kSecondsPerMinute = 60;
	constexpr int kMonthOffset = 1;				// tm_mon は 0 始まり
	constexpr int kYearOffset = 1900;			// tm_year は 1900 年からの年数

	// fopen と同じく他プロセスと共有可能なモードで開く
	FILE* OpenFile(LPCSTR file_name, LPCSTR mode)
	{
		return _fsopen(file_name, mode, _SH_DENYNO);
	}

	// set.ini から記録間隔（分）を読む。
	// FileName 行もあるが、ログファイル名は画面の指定を使うので読まない
	int LoadCycleFromIni(int default_cycle)
	{
		int cycle = default_cycle;
		FILE* fp = OpenFile(kIniFileName, "r");
		if (fp == nullptr)
		{
			return cycle;
		}

		char buff[kLineBufferSize] = {};
		while (fgets(buff, sizeof(buff), fp))
		{
			if (strncmp(buff, kIniComment, strlen(kIniComment)) == 0)
			{
				continue;
			}
			if (strncmp(buff, kIniKeyCycle, strlen(kIniKeyCycle)) == 0)
			{
				sscanf_s(buff, "Cycle=%d", &cycle);
			}
		}
		fclose(fp);
		return cycle;
	}

	// ログ1行分の日時文字列 "YYYY.MM.DD_hh:mm:ss"
	CStringA FormatTime(const tm& t)
	{
		CStringA text;
		text.Format("%04d.%02d.%02d_%02d:%02d:%02d",
			t.tm_year + kYearOffset,
			t.tm_mon + kMonthOffset,
			t.tm_mday,
			t.tm_hour,
			t.tm_min,
			t.tm_sec);
		return text;
	}

	bool IsValidDate(const tm& t)
	{
		const int year = t.tm_year + kYearOffset;
		const int month = t.tm_mon + kMonthOffset;
		return 0 < year
			&& 0 < month && month <= 12
			&& 0 < t.tm_mday && t.tm_mday <= 31
			&& 0 <= t.tm_hour && t.tm_hour < 24
			&& 0 <= t.tm_min && t.tm_min < 60
			&& 0 <= t.tm_sec && t.tm_sec < 60;
	}

	// ログ末尾付近を読み、最後の有効な日時を返す（無ければ 0 初期化のまま）
	tm ReadLastTime(FILE* fp)
	{
		tm last = {};
		tm parsed = {};
		char buff[kLineBufferSize] = {};

		fseek(fp, -kLineBufferSize, SEEK_END);
		while (fgets(buff, sizeof(buff), fp))
		{
			sscanf_s(buff, "%d.%02d.%02d_%02d:%02d:%02d",
				&parsed.tm_year,
				&parsed.tm_mon,
				&parsed.tm_mday,
				&parsed.tm_hour,
				&parsed.tm_min,
				&parsed.tm_sec);
			parsed.tm_mon -= kMonthOffset;
			parsed.tm_year -= kYearOffset;

			if (IsValidDate(parsed))
			{
				last = parsed;
			}
		}
		return last;
	}

	// 前回の記録から「記録間隔 ± 許容量」の範囲で実行されたか
	bool IsOnSchedule(tm last_time, tm now_time, int cycle_minutes)
	{
		const long long now = mktime(&now_time);
		const long long last = mktime(&last_time);
		const long long lag = llabs(now - (last + static_cast<long long>(cycle_minutes) * kSecondsPerMinute));
		return lag <= kAllowableLagSeconds;
	}
}

LoginHistoryDlg::LoginHistoryDlg(CWnd* parent /*=nullptr*/)
	: CDialogEx(LoginHistoryDlg::IDD, parent)
{
	icon_ = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void LoginHistoryDlg::DoDataExchange(CDataExchange* dx)
{
	CDialogEx::DoDataExchange(dx);
	DDX_Text(dx, IDET_LOGNAME, log_name_);
}

BEGIN_MESSAGE_MAP(LoginHistoryDlg, CDialogEx)
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	ON_BN_CLICKED(IDBT_EXEC, &LoginHistoryDlg::OnBnClickedExec)
END_MESSAGE_MAP()

BOOL LoginHistoryDlg::OnInitDialog()
{
	CDialogEx::OnInitDialog();

	SetIcon(icon_, TRUE);
	SetIcon(icon_, FALSE);

	log_name_ = kDefaultLogFileName;
	UpdateData(FALSE);

	return TRUE;
}

// 最小化時のアイコン描画（ダイアログはフレームワークが描いてくれないため）
void LoginHistoryDlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this);

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		const int icon_width = GetSystemMetrics(SM_CXICON);
		const int icon_height = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		const int x = (rect.Width() - icon_width + 1) / 2;
		const int y = (rect.Height() - icon_height + 1) / 2;

		dc.DrawIcon(x, y, icon_);
	}
	else
	{
		CDialogEx::OnPaint();
	}
}

HCURSOR LoginHistoryDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(icon_);
}

// 現在日時をログに追記する。前回からの間隔が設定とずれていたら行末に印を付ける
void LoginHistoryDlg::OnBnClickedExec()
{
	UpdateData();

	const int cycle = LoadCycleFromIni(kDefaultCycleMinutes);

	const time_t now = time(nullptr);
	tm now_time = {};
	localtime_s(&now_time, &now);

	FILE* fp = OpenFile(log_name_, "r+");
	if (fp == nullptr)
	{
		// 初回はファイルを作って現在日時だけを書く
		fp = OpenFile(log_name_, "w");
		if (fp != nullptr)
		{
			fprintf(fp, "%s\n", static_cast<LPCSTR>(FormatTime(now_time)));
			fclose(fp);
		}
		return;
	}

	CStringA line = FormatTime(now_time);
	if (!IsOnSchedule(ReadLastTime(fp), now_time, cycle))
	{
		line += kWarningMark;
	}

	// 読み込みから書き込みに切り替えるときは fseek が必要
	fseek(fp, 0, SEEK_END);
	fprintf(fp, "%s\n", static_cast<LPCSTR>(line));
	fclose(fp);
}
