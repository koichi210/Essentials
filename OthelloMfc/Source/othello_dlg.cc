// othello_dlg.cc : メインダイアログ（盤面の描画と操作）

#include "StdAfx.h"
#include "othello.h"
#include "othello_dlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

namespace
{
	// レイアウト(px)
	const int kFrameOffset		= 25;	// 画面端から盤までの距離
	const int kNumberOffset		= 5;	// 画面端から座標表記までの距離
	const int kInfoAreaWidth	= 100;	// 盤の右側の情報表示エリアの幅
	const int kBoardToInfoGap	= 20;	// 盤と情報表示エリアの距離
	const int kWindowMinSize	= 250;	// ウィンドウの最小サイズ
	const int kLabelExtraSize	= 15;	// 手番・スコア表示の拡張幅/高さ
	const int kLabelFontHeight	= 15;
	const int kLabelFontWeight	= 15;
	const COLORREF kBoardColor		= RGB(60, 150, 60);
	const COLORREF kPlayerMarkColor	= RGB(0, 0, 255);	// 人の手番の置ける場所
	const COLORREF kComMarkColor	= RGB(255, 0, 0);	// COMの手番の置ける場所

	// タイマ
	const UINT_PTR kComTimerId		= 1;
	const UINT_PTR kBlackTimerId	= 2;
	const UINT_PTR kWhiteTimerId	= 3;
	const UINT kComThinkInterval	= 10;	// COMが打つ間隔(ms)
	const UINT kCountDownCycle		= 100;	// 持ち時間の減算周期(ms)

	// 持ち時間
	const int kNoTimeLimit = -1;
	const struct
	{
		UINT	menu_id;
		int		seconds;
	} kTimeLimitMenus[] =
	{
		{ ID_MENUITEM_TIME_NONE,	kNoTimeLimit },
		{ ID_MENUITEM_TIME_05,		30 },
		{ ID_MENUITEM_TIME_1,		60 },
		{ ID_MENUITEM_TIME_2,		120 },
		{ ID_MENUITEM_TIME_3,		180 },
		{ ID_MENUITEM_TIME_5,		300 },
		{ ID_MENUITEM_TIME_10,		600 },
		{ ID_MENUITEM_TIME_15,		900 },
	};

	// 手番・スコア・残り時間ラベルの並び（直前のラベルからの縦の間隔）
	const struct
	{
		int		ctrl_id;
		int		gap_y;
	} kInfoLabels[] =
	{
		{ IDC_TURN_TEXT,		0 },
		{ IDC_SCORE,			20 },
		{ IDC_BLACK_TIME_NAME,	60 },
		{ IDC_BLACK_TIME,		20 },
		{ IDC_WHITE_TIME_NAME,	40 },
		{ IDC_WHITE_TIME,		20 },
	};

	const int kTimeLabelIds[] = { IDC_BLACK_TIME_NAME, IDC_BLACK_TIME, IDC_WHITE_TIME_NAME, IDC_WHITE_TIME };

	const LPCTSTR kKifuFilter = _T("オセロ棋譜ファイル (*.rkf)|*.rkf|All Files (*.*)|*.*||");

	UINT_PTR CountDownTimerId(Stone color)
	{
		return (color == Stone::kBlack) ? kBlackTimerId : kWhiteTimerId;
	}

	CString FormatRemainTime(int remain_ms)
	{
		const int seconds = remain_ms / 1000;
		CString text;
		text.Format(_T("%02d 分 %02d 秒"), seconds / 60, seconds % 60);
		return text;
	}
}

/////////////////////////////////////////////////////////////////////////////
// AboutDlg : バージョン情報

class AboutDlg : public CDialog
{
public:
	AboutDlg() : CDialog(IDD_ABOUTBOX) {}
};

/////////////////////////////////////////////////////////////////////////////
// OthelloDlg

OthelloDlg::OthelloDlg(CWnd* parent)
	: CDialog(IDD, parent)
	, icon_(AfxGetApp()->LoadIcon(IDR_MAINFRAME))
	, pressed_cell_(0, 0)
	, info_pos_(0, 0)
	, cell_size_(0)
	, time_limit_sec_(kNoTimeLimit)
	, black_remain_ms_(0)
	, white_remain_ms_(0)
{
}

BEGIN_MESSAGE_MAP(OthelloDlg, CDialog)
	ON_WM_SYSCOMMAND()
	ON_WM_QUERYDRAGICON()
	ON_WM_PAINT()
	ON_WM_GETMINMAXINFO()
	ON_WM_SIZE()
	ON_WM_TIMER()
	ON_WM_LBUTTONDOWN()
	ON_WM_LBUTTONUP()
	ON_COMMAND(ID_MENUITEM_START, &OthelloDlg::OnGameStart)
	ON_COMMAND(ID_MENUITEM_STOP, &OthelloDlg::OnGameStop)
	ON_COMMAND(ID_MENUITEM_RESET, &OthelloDlg::OnGameReset)
	ON_COMMAND(ID_MENUITEM_EXIT, &OthelloDlg::OnGameExit)
	ON_COMMAND(ID_MENUITEM_PUT_NOTICE, &OthelloDlg::OnToggleShowMovable)
	ON_COMMAND_RANGE(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, &OthelloDlg::OnPlayMode)
	ON_COMMAND_RANGE(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, &OthelloDlg::OnComLevel)
	ON_COMMAND_RANGE(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, &OthelloDlg::OnTimeLimit)
	ON_COMMAND(ID_MENUITEM_KIHU_SHOW, &OthelloDlg::OnKifuShow)
	ON_COMMAND(ID_MENUITEM_KIHU_SAVE, &OthelloDlg::OnKifuSave)
	ON_COMMAND(ID_MENUITEM_KIHU_READ, &OthelloDlg::OnKifuRead)
	ON_COMMAND(ID_MENUITEM_VERS, &OthelloDlg::OnRedo)
	ON_COMMAND(ID_MENUITEM_REVERS, &OthelloDlg::OnUndo)
	ON_COMMAND(ID_MENUITEM_Version, &OthelloDlg::OnVersion)
	ON_COMMAND(ID_MENUITEM_HOWTOPLAY, &OthelloDlg::OnHowToPlay)
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// Windows メッセージ

BOOL OthelloDlg::OnInitDialog()
{
	CDialog::OnInitDialog();

	// システムメニューに「バージョン情報」を追加
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);
	CMenu* sys_menu = GetSystemMenu(FALSE);
	if (sys_menu != nullptr)
	{
		CString about_menu;
		about_menu.LoadString(IDS_ABOUTBOX);
		if (!about_menu.IsEmpty())
		{
			sys_menu->AppendMenu(MF_SEPARATOR);
			sys_menu->AppendMenu(MF_STRING, IDM_ABOUTBOX, about_menu);
		}
	}

	SetIcon(icon_, TRUE);
	SetIcon(icon_, FALSE);

	// メニューの初期チェック
	CheckMenuInRange(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, ID_MENUITEM_PLAYMD_PP + static_cast<UINT>(game_.GetPlayMode()));
	CheckMenuInRange(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, ID_MENUITEM_ComLevel1 + game_.GetComLevel() - 1);
	CheckMenuInRange(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, ID_MENUITEM_TIME_NONE);
	CheckMenu(ID_MENUITEM_PUT_NOTICE, game_.GetShowMovable());

	// OK/キャンセルボタンは使わない
	GetDlgItem(IDOK)->ShowWindow(SW_HIDE);
	GetDlgItem(IDCANCEL)->ShowWindow(SW_HIDE);

	// 手番・スコア表示は大きめのフォントにする
	LOGFONT log_font = {};
	log_font.lfCharSet = DEFAULT_CHARSET;
	log_font.lfWeight = kLabelFontWeight;
	log_font.lfHeight = kLabelFontHeight;
	label_font_.CreateFontIndirect(&log_font);
	for (int ctrl_id : { IDC_TURN_TEXT, IDC_SCORE })
	{
		CWnd* label = GetDlgItem(ctrl_id);
		CRect rect;
		label->GetClientRect(&rect);
		label->SetWindowPos(nullptr, 0, 0, rect.right + kLabelExtraSize, rect.bottom + kLabelExtraSize, SWP_NOMOVE | SWP_NOZORDER);
		label->SetFont(&label_font_);
	}

	NewGame();
	UpdateLayout();
	UpdateUndoRedoMenus();

	return TRUE;
}

// Enter キーでダイアログを閉じない
void OthelloDlg::OnOK()
{
}

void OthelloDlg::OnSysCommand(UINT id, LPARAM param)
{
	if ((id & 0xFFF0) == IDM_ABOUTBOX)
	{
		OnVersion();
	}
	else
	{
		CDialog::OnSysCommand(id, param);
	}
}

HCURSOR OthelloDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(icon_);
}

void OthelloDlg::OnPaint()
{
	CPaintDC dc(this);

	if (IsIconic())
	{
		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		CRect rect;
		GetClientRect(&rect);
		const int x = (rect.Width() - GetSystemMetrics(SM_CXICON) + 1) / 2;
		const int y = (rect.Height() - GetSystemMetrics(SM_CYICON) + 1) / 2;
		dc.DrawIcon(x, y, icon_);
		return;
	}

	// 起動直後の OnSize ではコントロールの位置を決められないので、描画のたびに合わせる
	UpdateLabelPositions();
	UpdateTimeLabels();

	DrawBoard(dc);
	if (game_.GetShowMovable())
	{
		DrawMovableMarks(dc);
	}
}

void OthelloDlg::OnGetMinMaxInfo(MINMAXINFO* min_max_info)
{
	min_max_info->ptMinTrackSize.x = max(min_max_info->ptMinTrackSize.x, static_cast<LONG>(kWindowMinSize + kInfoAreaWidth));
	min_max_info->ptMinTrackSize.y = max(min_max_info->ptMinTrackSize.y, static_cast<LONG>(kWindowMinSize));
	CDialog::OnGetMinMaxInfo(min_max_info);
}

void OthelloDlg::OnSize(UINT type, int cx, int cy)
{
	UpdateLayout();
	Invalidate();
	CDialog::OnSize(type, cx, cy);
}

void OthelloDlg::OnTimer(UINT_PTR event_id)
{
	if (event_id == kComTimerId)
	{
		PlayComTurn();
	}
	else if (event_id == kBlackTimerId || event_id == kWhiteTimerId)
	{
		int& remain_ms = (event_id == kBlackTimerId) ? black_remain_ms_ : white_remain_ms_;
		remain_ms -= static_cast<int>(kCountDownCycle);
		UpdateTimeLabels();

		if (black_remain_ms_ <= 0 || white_remain_ms_ <= 0)
		{
			EndGame(true);
		}
	}

	CDialog::OnTimer(event_id);
}

void OthelloDlg::OnLButtonDown(UINT flags, CPoint point)
{
	pressed_cell_ = HitTestCell(point);
	CDialog::OnLButtonDown(flags, point);
}

// 押したマスと同じマスで離したときだけ打つ
void OthelloDlg::OnLButtonUp(UINT flags, CPoint point)
{
	const CPoint cell = HitTestCell(point);

	if (game_.GetGameState() != GameState::kEnd
		&& !game_.IsComTurn()
		&& cell == pressed_cell_
		&& game_.GetBoard().CanPut(cell, game_.GetTurn()))
	{
		if (game_.GetGameState() == GameState::kStop)
		{
			AfxMessageBox(_T("pls click [START] button on [GAME]"));
		}
		else
		{
			if (game_.GetGameState() == GameState::kInit)
			{
				StartGame();
			}
			PlayMove(cell);
			UpdateUndoRedoMenus();
		}
	}

	CDialog::OnLButtonUp(flags, point);
}

/////////////////////////////////////////////////////////////////////////////
// メニュー

void OthelloDlg::OnGameStart()
{
	StartGame();
	Invalidate();
}

void OthelloDlg::OnGameStop()
{
	KillAllTimers();

	if (game_.GetGameState() != GameState::kEnd)
	{
		game_.SetGameState(GameState::kStop);
		EnableMenu(ID_MENUITEM_START, true);
		EnableMenu(ID_MENUITEM_STOP, false);
	}
}

void OthelloDlg::OnGameReset()
{
	KillAllTimers();
	EnableTimeLimitMenus(true);
	NewGame();
	UpdateUndoRedoMenus();
}

void OthelloDlg::OnGameExit()
{
	OnCancel();
}

void OthelloDlg::OnToggleShowMovable()
{
	game_.SetShowMovable(!game_.GetShowMovable());
	CheckMenu(ID_MENUITEM_PUT_NOTICE, game_.GetShowMovable());
	Invalidate();
}

// メニューIDの並びは PlayMode の並びと同じ
void OthelloDlg::OnPlayMode(UINT id)
{
	game_.SetPlayMode(static_cast<PlayMode>(id - ID_MENUITEM_PLAYMD_PP));
	CheckMenuInRange(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, id);
}

void OthelloDlg::OnComLevel(UINT id)
{
	game_.SetComLevel(id - ID_MENUITEM_ComLevel1 + 1);
	CheckMenuInRange(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, id);
}

void OthelloDlg::OnTimeLimit(UINT id)
{
	for (const auto& menu : kTimeLimitMenus)
	{
		if (menu.menu_id == id)
		{
			SetTimeLimit(menu.seconds, id);
			return;
		}
	}
}

void OthelloDlg::OnKifuShow()
{
	AfxMessageBox(game_.GetKifuText());
}

void OthelloDlg::OnKifuSave()
{
	CFileDialog dialog(FALSE, _T("rkf"), nullptr, OFN_HIDEREADONLY | OFN_OVERWRITEPROMPT, kKifuFilter, this);
	if (dialog.DoModal() != IDOK)
	{
		return;
	}

	if (!game_.SaveKifu(dialog.GetPathName()))
	{
		AfxMessageBox(_T("ファイル書き込みエラー"));
	}
}

void OthelloDlg::OnKifuRead()
{
	CFileDialog dialog(TRUE, _T("rkf"), nullptr, OFN_HIDEREADONLY, kKifuFilter, this);
	if (dialog.DoModal() != IDOK)
	{
		return;
	}

	NewGame();
	if (!game_.LoadKifu(dialog.GetPathName()))
	{
		AfxMessageBox(_T("ファイル読み込みエラー"));
		NewGame();
		return;
	}

	// 最後に打った色の次の手番から再開
	const Stone next = game_.GetBoard().GetNextTurn(game_.GetTurn());
	if (next != Stone::kNone)
	{
		ChangeTurn(next);
	}
	SetTimer(kComTimerId, kComThinkInterval, nullptr);

	UpdateScore();
	UpdateUndoRedoMenus();
	Invalidate();
}

void OthelloDlg::OnRedo()
{
	if (game_.GetGameState() == GameState::kPlay || game_.GetGameState() == GameState::kStop)
	{
		OnGameStop();
		game_.Redo();
		UpdateScore();
		UpdateUndoRedoMenus();
		Invalidate();
	}
}

void OthelloDlg::OnUndo()
{
	if (game_.GetGameState() == GameState::kPlay || game_.GetGameState() == GameState::kStop)
	{
		OnGameStop();
		game_.Undo();
		UpdateScore();
		UpdateUndoRedoMenus();
		Invalidate();
	}
}

void OthelloDlg::OnVersion()
{
	AboutDlg dialog;
	dialog.DoModal();
}

void OthelloDlg::OnHowToPlay()
{
	AfxMessageBox(_T("普通のオセロです(｡-_-｡)"));
}

/////////////////////////////////////////////////////////////////////////////
// 対局の進行

void OthelloDlg::NewGame()
{
	game_.NewGame();
	SetWindowText(_T("othello"));

	black_remain_ms_ = time_limit_sec_ * 1000;
	white_remain_ms_ = time_limit_sec_ * 1000;

	EnableMenu(ID_MENUITEM_START, true);
	EnableMenu(ID_MENUITEM_STOP, false);

	Invalidate();
	UpdateScore();
}

void OthelloDlg::StartGame()
{
	if (game_.GetGameState() == GameState::kEnd)
	{
		NewGame();
	}
	game_.SetGameState(GameState::kPlay);

	if (time_limit_sec_ != kNoTimeLimit)
	{
		SetTimer(CountDownTimerId(game_.GetTurn()), kCountDownCycle, nullptr);
	}
	SetTimer(kComTimerId, kComThinkInterval, nullptr);

	EnableMenu(ID_MENUITEM_START, false);
	EnableMenu(ID_MENUITEM_STOP, true);
	EnableTimeLimitMenus(false);
}

void OthelloDlg::EndGame(bool timeout)
{
	game_.SetGameState(GameState::kEnd);
	KillAllTimers();

	EnableMenu(ID_MENUITEM_START, true);
	EnableMenu(ID_MENUITEM_STOP, false);
	EnableTimeLimitMenus(true);
	UpdateScore();

	CString message;
	if (timeout)
	{
		const Stone loser = (black_remain_ms_ <= 0) ? Stone::kBlack : Stone::kWhite;
		message.Format(_T("タイムアウト\n%sの負けです。\n\nちなみに・・・"), ColorName(loser));
		AfxMessageBox(message);
	}

	int black = 0;
	int white = 0;
	game_.GetBoard().CountStones(black, white);
	message.Format(_T("黒：%d　白：%d\n ゲーム終了"), black, white);
	AfxMessageBox(message);

	SetWindowText(_T("othello  終了"));
}

// 打って手番を進める（打てない場所なら false）
bool OthelloDlg::PlayMove(CPoint pos)
{
	if (!game_.PlayMove(pos))
	{
		if (game_.GetPlayMode() != PlayMode::kComVsCom)
		{
			AfxMessageBox(_T("置くところ不正"));
		}
		return false;
	}
	Invalidate();

	const Stone next = game_.GetBoard().GetNextTurn(game_.GetTurn());
	if (next == Stone::kNone)
	{
		EndGame(false);
	}
	else
	{
		ChangeTurn(next);
		UpdateScore();
	}
	return true;
}

void OthelloDlg::PlayComTurn()
{
	if (!game_.IsComTurn())
	{
		return;
	}

	CPoint pos;
	if (!com_.Think(game_.GetBoard(), game_.GetTurn(), game_.GetComLevel(), game_.GetMoveCount(), pos))
	{
		KillTimer(kComTimerId);
		CString message;
		message.Format(_T("COM 置くところ不正(x=%d y=%d)"), pos.x, pos.y);
		AfxMessageBox(message);
		return;
	}

	PlayMove(pos);
	UpdateUndoRedoMenus();
}

// next が今の手番と同じなら、相手はパス
void OthelloDlg::ChangeTurn(Stone next)
{
	const Stone current = game_.GetTurn();
	if (next == current)
	{
		if (time_limit_sec_ != kNoTimeLimit)
		{
			KillTimer(CountDownTimerId(current));
		}
		KillTimer(kComTimerId);

		CString message;
		message.Format(_T("%sは置くところがありません。"), ColorName(Opponent(current)));
		AfxMessageBox(message);

		SetTimer(kComTimerId, kComThinkInterval, nullptr);
	}

	game_.SetTurn(next);
	StartCountDown(next);
}

// 手番側の持ち時間だけを減らす
void OthelloDlg::StartCountDown(Stone color)
{
	if (time_limit_sec_ == kNoTimeLimit)
	{
		return;
	}

	KillTimer(CountDownTimerId(Opponent(color)));
	SetTimer(CountDownTimerId(color), kCountDownCycle, nullptr);
}

void OthelloDlg::KillAllTimers()
{
	KillTimer(kBlackTimerId);
	KillTimer(kWhiteTimerId);
	KillTimer(kComTimerId);
}

void OthelloDlg::SetTimeLimit(int seconds, UINT menu_id)
{
	time_limit_sec_ = seconds;
	black_remain_ms_ = seconds * 1000;
	white_remain_ms_ = seconds * 1000;

	CheckMenuInRange(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, menu_id);

	const int show = (seconds == kNoTimeLimit) ? SW_HIDE : SW_SHOW;
	for (int ctrl_id : kTimeLabelIds)
	{
		GetDlgItem(ctrl_id)->ShowWindow(show);
	}

	UpdateTimeLabels();
}

/////////////////////////////////////////////////////////////////////////////
// 画面の更新

// クライアント領域に収まる最大のマスの大きさを求める
void OthelloDlg::UpdateLayout()
{
	CRect client;
	GetClientRect(&client);

	const int board_width = client.Width() - kFrameOffset * 2 - kInfoAreaWidth;
	const int board_height = client.Height() - kFrameOffset * 2;
	cell_size_ = min(board_width, board_height) / kBoardSize;

	info_pos_.x = cell_size_ * kBoardSize + kFrameOffset + kBoardToInfoGap;
	info_pos_.y = cell_size_;
}

void OthelloDlg::UpdateLabelPositions()
{
	CPoint pos = info_pos_;

	for (const auto& label : kInfoLabels)
	{
		pos.y += label.gap_y;
		GetDlgItem(label.ctrl_id)->SetWindowPos(nullptr, pos.x, pos.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
	}
}

void OthelloDlg::UpdateTimeLabels()
{
	SetDlgItemText(IDC_BLACK_TIME, FormatRemainTime(black_remain_ms_));
	SetDlgItemText(IDC_WHITE_TIME, FormatRemainTime(white_remain_ms_));
}

void OthelloDlg::UpdateScore()
{
	CString text;
	text.Format(_T(" 【%sの番】"), ColorName(game_.GetTurn()));
	SetDlgItemText(IDC_TURN_TEXT, text);

	int black = 0;
	int white = 0;
	game_.GetBoard().CountStones(black, white);
	text.Format(_T("黒：%2d　白：%2d "), black, white);
	SetDlgItemText(IDC_SCORE, text);
}

void OthelloDlg::UpdateUndoRedoMenus()
{
	EnableMenu(ID_MENUITEM_VERS, game_.CanRedo());
	EnableMenu(ID_MENUITEM_REVERS, game_.CanUndo());
}

// 対局中は持ち時間を変更させない
void OthelloDlg::EnableTimeLimitMenus(bool enable)
{
	for (const auto& menu : kTimeLimitMenus)
	{
		EnableMenu(menu.menu_id, enable);
	}
}

void OthelloDlg::EnableMenu(UINT id, bool enable)
{
	GetMenu()->EnableMenuItem(id, MF_BYCOMMAND | (enable ? MF_ENABLED : MF_GRAYED));
}

void OthelloDlg::CheckMenu(UINT id, bool check)
{
	GetMenu()->CheckMenuItem(id, MF_BYCOMMAND | (check ? MF_CHECKED : MF_UNCHECKED));
}

void OthelloDlg::CheckMenuInRange(UINT first_id, UINT last_id, UINT check_id)
{
	for (UINT id = first_id; id <= last_id; id++)
	{
		CheckMenu(id, id == check_id);
	}
}

/////////////////////////////////////////////////////////////////////////////
// 描画

void OthelloDlg::DrawBoard(CDC& dc)
{
	const int board_end = kBoardSize * cell_size_ + kFrameOffset;

	// 背景
	CBrush board_brush(kBoardColor);
	CBrush* old_brush = dc.SelectObject(&board_brush);
	dc.Rectangle(kFrameOffset, kFrameOffset, board_end, board_end);
	dc.SelectObject(old_brush);

	// 罫線
	CPen pen(PS_SOLID, 1, RGB(0, 0, 0));
	CPen* old_pen = dc.SelectObject(&pen);
	for (int i = 0; i <= kBoardSize; i++)
	{
		const int line = i * cell_size_ + kFrameOffset;
		dc.MoveTo(line, kFrameOffset);
		dc.LineTo(line, board_end);
		dc.MoveTo(kFrameOffset, line);
		dc.LineTo(board_end, line);
	}
	dc.SelectObject(old_pen);

	// 座標表記（列 A〜H / 行 1〜8）。4 と 9 は文字位置の微調整
	const int old_bk_mode = dc.SetBkMode(TRANSPARENT);
	for (int i = 1; i <= kBoardSize; i++)
	{
		const int center = i * cell_size_ + kFrameOffset - cell_size_ / 2;
		dc.TextOut(center - 4, kNumberOffset, CString(ColumnChar(i)));
		CString text;
		text.Format(_T("%d"), i);
		dc.TextOut(kNumberOffset, center - 9, text);
	}
	dc.SetBkMode(old_bk_mode);

	// 石
	const Board& board = game_.GetBoard();
	for (int y = 1; y <= kBoardSize; y++)
	{
		for (int x = 1; x <= kBoardSize; x++)
		{
			const Stone stone = board.GetAt(CPoint(x, y));
			if (stone != Stone::kNone)
			{
				DrawStone(dc, CPoint(x, y), stone);
			}
		}
	}
}

void OthelloDlg::DrawStone(CDC& dc, CPoint cell, Stone color)
{
	// 石の直径はマスの約9割
	const int margin = cell_size_ - cell_size_ * 95 / 100;
	const CRect rect(
		(cell.x - 1) * cell_size_ + margin + kFrameOffset,
		(cell.y - 1) * cell_size_ + margin + kFrameOffset,
		cell.x * cell_size_ - margin + kFrameOffset,
		cell.y * cell_size_ - margin + kFrameOffset);

	CBrush brush((color == Stone::kBlack) ? RGB(0, 0, 0) : RGB(255, 255, 255));
	CBrush* old_brush = dc.SelectObject(&brush);
	dc.Ellipse(&rect);
	dc.SelectObject(old_brush);
}

// 置ける場所に小さな点を描く（人の手番は青、COMの手番は赤）
void OthelloDlg::DrawMovableMarks(CDC& dc)
{
	CBrush brush(game_.IsComTurn() ? kComMarkColor : kPlayerMarkColor);
	CBrush* old_brush = dc.SelectObject(&brush);

	for (const CPoint& cell : game_.GetBoard().GetMovablePositions(game_.GetTurn()))
	{
		const int left = (cell.x - 1) * cell_size_ + cell_size_ / 2 - 2 + kFrameOffset;
		const int top = (cell.y - 1) * cell_size_ + cell_size_ / 2 - 2 + kFrameOffset;
		dc.Ellipse(left, top, left + 4, top + 4);
	}

	dc.SelectObject(old_brush);
}

CPoint OthelloDlg::HitTestCell(CPoint point) const
{
	if (cell_size_ <= 0 || point.x < kFrameOffset || point.y < kFrameOffset)
	{
		return CPoint(0, 0);
	}

	const CPoint cell((point.x - kFrameOffset) / cell_size_ + 1, (point.y - kFrameOffset) / cell_size_ + 1);
	return Board::IsInside(cell) ? cell : CPoint(0, 0);
}
