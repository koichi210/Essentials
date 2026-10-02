// OthelloDlg.cpp : メインダイアログ（盤面の描画と操作）

#include "StdAfx.h"
#include "Othello.h"
#include "OthelloDlg.h"

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
		UINT	menuId;
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
		int		ctrlId;
		int		gapY;
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
		return (color == Stone::Black) ? kBlackTimerId : kWhiteTimerId;
	}

	CString FormatRemainTime(int remainMs)
	{
		const int seconds = remainMs / 1000;
		CString text;
		text.Format(_T("%02d 分 %02d 秒"), seconds / 60, seconds % 60);
		return text;
	}
}

/////////////////////////////////////////////////////////////////////////////
// CAboutDlg : バージョン情報

class CAboutDlg : public CDialog
{
public:
	CAboutDlg() : CDialog(IDD_ABOUTBOX) {}
};

/////////////////////////////////////////////////////////////////////////////
// COthelloDlg

COthelloDlg::COthelloDlg(CWnd* pParent)
	: CDialog(IDD, pParent)
	, m_hIcon(AfxGetApp()->LoadIcon(IDR_MAINFRAME))
	, m_pressedCell(0, 0)
	, m_infoPos(0, 0)
	, m_cellSize(0)
	, m_timeLimitSec(kNoTimeLimit)
	, m_blackRemainMs(0)
	, m_whiteRemainMs(0)
{
}

BEGIN_MESSAGE_MAP(COthelloDlg, CDialog)
	ON_WM_SYSCOMMAND()
	ON_WM_QUERYDRAGICON()
	ON_WM_PAINT()
	ON_WM_GETMINMAXINFO()
	ON_WM_SIZE()
	ON_WM_TIMER()
	ON_WM_LBUTTONDOWN()
	ON_WM_LBUTTONUP()
	ON_COMMAND(ID_MENUITEM_START, &COthelloDlg::OnGameStart)
	ON_COMMAND(ID_MENUITEM_STOP, &COthelloDlg::OnGameStop)
	ON_COMMAND(ID_MENUITEM_RESET, &COthelloDlg::OnGameReset)
	ON_COMMAND(ID_MENUITEM_EXIT, &COthelloDlg::OnGameExit)
	ON_COMMAND(ID_MENUITEM_PUT_NOTICE, &COthelloDlg::OnToggleShowMovable)
	ON_COMMAND_RANGE(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, &COthelloDlg::OnPlayMode)
	ON_COMMAND_RANGE(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, &COthelloDlg::OnComLevel)
	ON_COMMAND_RANGE(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, &COthelloDlg::OnTimeLimit)
	ON_COMMAND(ID_MENUITEM_KIHU_SHOW, &COthelloDlg::OnKifuShow)
	ON_COMMAND(ID_MENUITEM_KIHU_SAVE, &COthelloDlg::OnKifuSave)
	ON_COMMAND(ID_MENUITEM_KIHU_READ, &COthelloDlg::OnKifuRead)
	ON_COMMAND(ID_MENUITEM_VERS, &COthelloDlg::OnRedo)
	ON_COMMAND(ID_MENUITEM_REVERS, &COthelloDlg::OnUndo)
	ON_COMMAND(ID_MENUITEM_Version, &COthelloDlg::OnVersion)
	ON_COMMAND(ID_MENUITEM_HOWTOPLAY, &COthelloDlg::OnHowToPlay)
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// Windows メッセージ

BOOL COthelloDlg::OnInitDialog()
{
	CDialog::OnInitDialog();

	// システムメニューに「バージョン情報」を追加
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);
	CMenu* pSysMenu = GetSystemMenu(FALSE);
	if (pSysMenu != nullptr)
	{
		CString aboutMenu;
		aboutMenu.LoadString(IDS_ABOUTBOX);
		if (!aboutMenu.IsEmpty())
		{
			pSysMenu->AppendMenu(MF_SEPARATOR);
			pSysMenu->AppendMenu(MF_STRING, IDM_ABOUTBOX, aboutMenu);
		}
	}

	SetIcon(m_hIcon, TRUE);
	SetIcon(m_hIcon, FALSE);

	// メニューの初期チェック
	CheckMenuInRange(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, ID_MENUITEM_PLAYMD_PP + static_cast<UINT>(m_game.m_playMode));
	CheckMenuInRange(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, ID_MENUITEM_ComLevel1 + m_game.m_comLevel - 1);
	CheckMenuInRange(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, ID_MENUITEM_TIME_NONE);
	CheckMenu(ID_MENUITEM_PUT_NOTICE, m_game.m_showMovable);

	// OK/キャンセルボタンは使わない
	GetDlgItem(IDOK)->ShowWindow(SW_HIDE);
	GetDlgItem(IDCANCEL)->ShowWindow(SW_HIDE);

	// 手番・スコア表示は大きめのフォントにする
	LOGFONT logFont = {};
	logFont.lfCharSet = DEFAULT_CHARSET;
	logFont.lfWeight = kLabelFontWeight;
	logFont.lfHeight = kLabelFontHeight;
	m_labelFont.CreateFontIndirect(&logFont);
	for (int ctrlId : { IDC_TURN_TEXT, IDC_SCORE })
	{
		CWnd* label = GetDlgItem(ctrlId);
		CRect rect;
		label->GetClientRect(&rect);
		label->SetWindowPos(nullptr, 0, 0, rect.right + kLabelExtraSize, rect.bottom + kLabelExtraSize, SWP_NOMOVE | SWP_NOZORDER);
		label->SetFont(&m_labelFont);
	}

	NewGame();
	UpdateLayout();
	UpdateUndoRedoMenus();

	return TRUE;
}

// Enter キーでダイアログを閉じない
void COthelloDlg::OnOK()
{
}

void COthelloDlg::OnSysCommand(UINT nID, LPARAM lParam)
{
	if ((nID & 0xFFF0) == IDM_ABOUTBOX)
	{
		OnVersion();
	}
	else
	{
		CDialog::OnSysCommand(nID, lParam);
	}
}

HCURSOR COthelloDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}

void COthelloDlg::OnPaint()
{
	CPaintDC dc(this);

	if (IsIconic())
	{
		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		CRect rect;
		GetClientRect(&rect);
		const int x = (rect.Width() - GetSystemMetrics(SM_CXICON) + 1) / 2;
		const int y = (rect.Height() - GetSystemMetrics(SM_CYICON) + 1) / 2;
		dc.DrawIcon(x, y, m_hIcon);
		return;
	}

	// 起動直後の OnSize ではコントロールの位置を決められないので、描画のたびに合わせる
	UpdateLabelPositions();
	UpdateTimeLabels();

	DrawBoard(dc);
	if (m_game.m_showMovable)
	{
		DrawMovableMarks(dc);
	}
}

void COthelloDlg::OnGetMinMaxInfo(MINMAXINFO* lpMMI)
{
	lpMMI->ptMinTrackSize.x = max(lpMMI->ptMinTrackSize.x, static_cast<LONG>(kWindowMinSize + kInfoAreaWidth));
	lpMMI->ptMinTrackSize.y = max(lpMMI->ptMinTrackSize.y, static_cast<LONG>(kWindowMinSize));
	CDialog::OnGetMinMaxInfo(lpMMI);
}

void COthelloDlg::OnSize(UINT nType, int cx, int cy)
{
	UpdateLayout();
	Invalidate();
	CDialog::OnSize(nType, cx, cy);
}

void COthelloDlg::OnTimer(UINT_PTR nIDEvent)
{
	if (nIDEvent == kComTimerId)
	{
		PlayComTurn();
	}
	else if (nIDEvent == kBlackTimerId || nIDEvent == kWhiteTimerId)
	{
		int& remainMs = (nIDEvent == kBlackTimerId) ? m_blackRemainMs : m_whiteRemainMs;
		remainMs -= static_cast<int>(kCountDownCycle);
		UpdateTimeLabels();

		if (m_blackRemainMs <= 0 || m_whiteRemainMs <= 0)
		{
			EndGame(true);
		}
	}

	CDialog::OnTimer(nIDEvent);
}

void COthelloDlg::OnLButtonDown(UINT nFlags, CPoint point)
{
	m_pressedCell = HitTestCell(point);
	CDialog::OnLButtonDown(nFlags, point);
}

// 押したマスと同じマスで離したときだけ打つ
void COthelloDlg::OnLButtonUp(UINT nFlags, CPoint point)
{
	const CPoint cell = HitTestCell(point);

	if (m_game.m_gameState != GameState::End
		&& !m_game.IsComTurn()
		&& cell == m_pressedCell
		&& m_game.GetBoard().CanPut(cell, m_game.GetTurn()))
	{
		if (m_game.m_gameState == GameState::Stop)
		{
			AfxMessageBox(_T("pls click [START] button on [GAME]"));
		}
		else
		{
			if (m_game.m_gameState == GameState::Init)
			{
				StartGame();
			}
			PlayMove(cell);
			UpdateUndoRedoMenus();
		}
	}

	CDialog::OnLButtonUp(nFlags, point);
}

/////////////////////////////////////////////////////////////////////////////
// メニュー

void COthelloDlg::OnGameStart()
{
	StartGame();
	Invalidate();
}

void COthelloDlg::OnGameStop()
{
	KillAllTimers();

	if (m_game.m_gameState != GameState::End)
	{
		m_game.m_gameState = GameState::Stop;
		EnableMenu(ID_MENUITEM_START, true);
		EnableMenu(ID_MENUITEM_STOP, false);
	}
}

void COthelloDlg::OnGameReset()
{
	KillAllTimers();
	EnableTimeLimitMenus(true);
	NewGame();
	UpdateUndoRedoMenus();
}

void COthelloDlg::OnGameExit()
{
	OnCancel();
}

void COthelloDlg::OnToggleShowMovable()
{
	m_game.m_showMovable = !m_game.m_showMovable;
	CheckMenu(ID_MENUITEM_PUT_NOTICE, m_game.m_showMovable);
	Invalidate();
}

// メニューIDの並びは PlayMode の並びと同じ
void COthelloDlg::OnPlayMode(UINT nID)
{
	m_game.m_playMode = static_cast<PlayMode>(nID - ID_MENUITEM_PLAYMD_PP);
	CheckMenuInRange(ID_MENUITEM_PLAYMD_PP, ID_MENUITEM_PLAYMD_CC, nID);
}

void COthelloDlg::OnComLevel(UINT nID)
{
	m_game.m_comLevel = nID - ID_MENUITEM_ComLevel1 + 1;
	CheckMenuInRange(ID_MENUITEM_ComLevel1, ID_MENUITEM_ComLevel3, nID);
}

void COthelloDlg::OnTimeLimit(UINT nID)
{
	for (const auto& menu : kTimeLimitMenus)
	{
		if (menu.menuId == nID)
		{
			SetTimeLimit(menu.seconds, nID);
			return;
		}
	}
}

void COthelloDlg::OnKifuShow()
{
	AfxMessageBox(m_game.GetKifuText());
}

void COthelloDlg::OnKifuSave()
{
	CFileDialog dialog(FALSE, _T("rkf"), nullptr, OFN_HIDEREADONLY | OFN_OVERWRITEPROMPT, kKifuFilter, this);
	if (dialog.DoModal() != IDOK)
	{
		return;
	}

	if (!m_game.SaveKifu(dialog.GetPathName()))
	{
		AfxMessageBox(_T("ファイル書き込みエラー"));
	}
}

void COthelloDlg::OnKifuRead()
{
	CFileDialog dialog(TRUE, _T("rkf"), nullptr, OFN_HIDEREADONLY, kKifuFilter, this);
	if (dialog.DoModal() != IDOK)
	{
		return;
	}

	NewGame();
	if (!m_game.LoadKifu(dialog.GetPathName()))
	{
		AfxMessageBox(_T("ファイル読み込みエラー"));
		NewGame();
		return;
	}

	// 最後に打った色の次の手番から再開
	const Stone next = m_game.GetBoard().GetNextTurn(m_game.GetTurn());
	if (next != Stone::None)
	{
		ChangeTurn(next);
	}
	SetTimer(kComTimerId, kComThinkInterval, nullptr);

	UpdateScore();
	UpdateUndoRedoMenus();
	Invalidate();
}

void COthelloDlg::OnRedo()
{
	if (m_game.m_gameState == GameState::Play || m_game.m_gameState == GameState::Stop)
	{
		OnGameStop();
		m_game.Redo();
		UpdateScore();
		UpdateUndoRedoMenus();
		Invalidate();
	}
}

void COthelloDlg::OnUndo()
{
	if (m_game.m_gameState == GameState::Play || m_game.m_gameState == GameState::Stop)
	{
		OnGameStop();
		m_game.Undo();
		UpdateScore();
		UpdateUndoRedoMenus();
		Invalidate();
	}
}

void COthelloDlg::OnVersion()
{
	CAboutDlg dialog;
	dialog.DoModal();
}

void COthelloDlg::OnHowToPlay()
{
	AfxMessageBox(_T("普通のオセロです(｡-_-｡)"));
}

/////////////////////////////////////////////////////////////////////////////
// 対局の進行

void COthelloDlg::NewGame()
{
	m_game.NewGame();
	SetWindowText(_T("othello"));

	m_blackRemainMs = m_timeLimitSec * 1000;
	m_whiteRemainMs = m_timeLimitSec * 1000;

	EnableMenu(ID_MENUITEM_START, true);
	EnableMenu(ID_MENUITEM_STOP, false);

	Invalidate();
	UpdateScore();
}

void COthelloDlg::StartGame()
{
	if (m_game.m_gameState == GameState::End)
	{
		NewGame();
	}
	m_game.m_gameState = GameState::Play;

	if (m_timeLimitSec != kNoTimeLimit)
	{
		SetTimer(CountDownTimerId(m_game.GetTurn()), kCountDownCycle, nullptr);
	}
	SetTimer(kComTimerId, kComThinkInterval, nullptr);

	EnableMenu(ID_MENUITEM_START, false);
	EnableMenu(ID_MENUITEM_STOP, true);
	EnableTimeLimitMenus(false);
}

void COthelloDlg::EndGame(bool timeout)
{
	m_game.m_gameState = GameState::End;
	KillAllTimers();

	EnableMenu(ID_MENUITEM_START, true);
	EnableMenu(ID_MENUITEM_STOP, false);
	EnableTimeLimitMenus(true);
	UpdateScore();

	CString message;
	if (timeout)
	{
		const Stone loser = (m_blackRemainMs <= 0) ? Stone::Black : Stone::White;
		message.Format(_T("タイムアウト\n%sの負けです。\n\nちなみに・・・"), ColorName(loser));
		AfxMessageBox(message);
	}

	int black = 0;
	int white = 0;
	m_game.GetBoard().CountStones(black, white);
	message.Format(_T("黒：%d　白：%d\n ゲーム終了"), black, white);
	AfxMessageBox(message);

	SetWindowText(_T("othello  終了"));
}

// 打って手番を進める（打てない場所なら false）
bool COthelloDlg::PlayMove(CPoint pos)
{
	if (!m_game.PlayMove(pos))
	{
		if (m_game.m_playMode != PlayMode::ComVsCom)
		{
			AfxMessageBox(_T("置くところ不正"));
		}
		return false;
	}
	Invalidate();

	const Stone next = m_game.GetBoard().GetNextTurn(m_game.GetTurn());
	if (next == Stone::None)
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

void COthelloDlg::PlayComTurn()
{
	if (!m_game.IsComTurn())
	{
		return;
	}

	CPoint pos;
	if (!m_com.Think(m_game.GetBoard(), m_game.GetTurn(), m_game.m_comLevel, m_game.GetMoveCount(), pos))
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
void COthelloDlg::ChangeTurn(Stone next)
{
	const Stone current = m_game.GetTurn();
	if (next == current)
	{
		if (m_timeLimitSec != kNoTimeLimit)
		{
			KillTimer(CountDownTimerId(current));
		}
		KillTimer(kComTimerId);

		CString message;
		message.Format(_T("%sは置くところがありません。"), ColorName(Opponent(current)));
		AfxMessageBox(message);

		SetTimer(kComTimerId, kComThinkInterval, nullptr);
	}

	m_game.SetTurn(next);
	StartCountDown(next);
}

// 手番側の持ち時間だけを減らす
void COthelloDlg::StartCountDown(Stone color)
{
	if (m_timeLimitSec == kNoTimeLimit)
	{
		return;
	}

	KillTimer(CountDownTimerId(Opponent(color)));
	SetTimer(CountDownTimerId(color), kCountDownCycle, nullptr);
}

void COthelloDlg::KillAllTimers()
{
	KillTimer(kBlackTimerId);
	KillTimer(kWhiteTimerId);
	KillTimer(kComTimerId);
}

void COthelloDlg::SetTimeLimit(int seconds, UINT menuId)
{
	m_timeLimitSec = seconds;
	m_blackRemainMs = seconds * 1000;
	m_whiteRemainMs = seconds * 1000;

	CheckMenuInRange(ID_MENUITEM_TIME_NONE, ID_MENUITEM_TIME_15, menuId);

	const int show = (seconds == kNoTimeLimit) ? SW_HIDE : SW_SHOW;
	for (int ctrlId : kTimeLabelIds)
	{
		GetDlgItem(ctrlId)->ShowWindow(show);
	}

	UpdateTimeLabels();
}

/////////////////////////////////////////////////////////////////////////////
// 画面の更新

// クライアント領域に収まる最大のマスの大きさを求める
void COthelloDlg::UpdateLayout()
{
	CRect client;
	GetClientRect(&client);

	const int boardWidth = client.Width() - kFrameOffset * 2 - kInfoAreaWidth;
	const int boardHeight = client.Height() - kFrameOffset * 2;
	m_cellSize = min(boardWidth, boardHeight) / kBoardSize;

	m_infoPos.x = m_cellSize * kBoardSize + kFrameOffset + kBoardToInfoGap;
	m_infoPos.y = m_cellSize;
}

void COthelloDlg::UpdateLabelPositions()
{
	CPoint pos = m_infoPos;

	for (const auto& label : kInfoLabels)
	{
		pos.y += label.gapY;
		GetDlgItem(label.ctrlId)->SetWindowPos(nullptr, pos.x, pos.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
	}
}

void COthelloDlg::UpdateTimeLabels()
{
	SetDlgItemText(IDC_BLACK_TIME, FormatRemainTime(m_blackRemainMs));
	SetDlgItemText(IDC_WHITE_TIME, FormatRemainTime(m_whiteRemainMs));
}

void COthelloDlg::UpdateScore()
{
	CString text;
	text.Format(_T(" 【%sの番】"), ColorName(m_game.GetTurn()));
	SetDlgItemText(IDC_TURN_TEXT, text);

	int black = 0;
	int white = 0;
	m_game.GetBoard().CountStones(black, white);
	text.Format(_T("黒：%2d　白：%2d "), black, white);
	SetDlgItemText(IDC_SCORE, text);
}

void COthelloDlg::UpdateUndoRedoMenus()
{
	EnableMenu(ID_MENUITEM_VERS, m_game.CanRedo());
	EnableMenu(ID_MENUITEM_REVERS, m_game.CanUndo());
}

// 対局中は持ち時間を変更させない
void COthelloDlg::EnableTimeLimitMenus(bool enable)
{
	for (const auto& menu : kTimeLimitMenus)
	{
		EnableMenu(menu.menuId, enable);
	}
}

void COthelloDlg::EnableMenu(UINT id, bool enable)
{
	GetMenu()->EnableMenuItem(id, MF_BYCOMMAND | (enable ? MF_ENABLED : MF_GRAYED));
}

void COthelloDlg::CheckMenu(UINT id, bool check)
{
	GetMenu()->CheckMenuItem(id, MF_BYCOMMAND | (check ? MF_CHECKED : MF_UNCHECKED));
}

void COthelloDlg::CheckMenuInRange(UINT firstId, UINT lastId, UINT checkId)
{
	for (UINT id = firstId; id <= lastId; id++)
	{
		CheckMenu(id, id == checkId);
	}
}

/////////////////////////////////////////////////////////////////////////////
// 描画

void COthelloDlg::DrawBoard(CDC& dc)
{
	const int boardEnd = kBoardSize * m_cellSize + kFrameOffset;

	// 背景
	CBrush boardBrush(kBoardColor);
	CBrush* oldBrush = dc.SelectObject(&boardBrush);
	dc.Rectangle(kFrameOffset, kFrameOffset, boardEnd, boardEnd);
	dc.SelectObject(oldBrush);

	// 罫線
	CPen pen(PS_SOLID, 1, RGB(0, 0, 0));
	CPen* oldPen = dc.SelectObject(&pen);
	for (int i = 0; i <= kBoardSize; i++)
	{
		const int line = i * m_cellSize + kFrameOffset;
		dc.MoveTo(line, kFrameOffset);
		dc.LineTo(line, boardEnd);
		dc.MoveTo(kFrameOffset, line);
		dc.LineTo(boardEnd, line);
	}
	dc.SelectObject(oldPen);

	// 座標表記（列 A〜H / 行 1〜8）。4 と 9 は文字位置の微調整
	const int oldBkMode = dc.SetBkMode(TRANSPARENT);
	for (int i = 1; i <= kBoardSize; i++)
	{
		const int center = i * m_cellSize + kFrameOffset - m_cellSize / 2;
		CString text;
		text.Format(_T("%c"), _T('A') + i - 1);
		dc.TextOut(center - 4, kNumberOffset, text);
		text.Format(_T("%d"), i);
		dc.TextOut(kNumberOffset, center - 9, text);
	}
	dc.SetBkMode(oldBkMode);

	// 石
	const CBoard& board = m_game.GetBoard();
	for (int y = 1; y <= kBoardSize; y++)
	{
		for (int x = 1; x <= kBoardSize; x++)
		{
			const Stone stone = board.GetAt(CPoint(x, y));
			if (stone != Stone::None)
			{
				DrawStone(dc, CPoint(x, y), stone);
			}
		}
	}
}

void COthelloDlg::DrawStone(CDC& dc, CPoint cell, Stone color)
{
	// 石の直径はマスの約9割
	const int margin = m_cellSize - m_cellSize * 95 / 100;
	const CRect rect(
		(cell.x - 1) * m_cellSize + margin + kFrameOffset,
		(cell.y - 1) * m_cellSize + margin + kFrameOffset,
		cell.x * m_cellSize - margin + kFrameOffset,
		cell.y * m_cellSize - margin + kFrameOffset);

	CBrush brush((color == Stone::Black) ? RGB(0, 0, 0) : RGB(255, 255, 255));
	CBrush* oldBrush = dc.SelectObject(&brush);
	dc.Ellipse(&rect);
	dc.SelectObject(oldBrush);
}

// 置ける場所に小さな点を描く（人の手番は青、COMの手番は赤）
void COthelloDlg::DrawMovableMarks(CDC& dc)
{
	CBrush brush(m_game.IsComTurn() ? kComMarkColor : kPlayerMarkColor);
	CBrush* oldBrush = dc.SelectObject(&brush);

	for (const CPoint& cell : m_game.GetBoard().GetMovablePositions(m_game.GetTurn()))
	{
		const int left = (cell.x - 1) * m_cellSize + m_cellSize / 2 - 2 + kFrameOffset;
		const int top = (cell.y - 1) * m_cellSize + m_cellSize / 2 - 2 + kFrameOffset;
		dc.Ellipse(left, top, left + 4, top + 4);
	}

	dc.SelectObject(oldBrush);
}

CPoint COthelloDlg::HitTestCell(CPoint point) const
{
	if (m_cellSize <= 0 || point.x < kFrameOffset || point.y < kFrameOffset)
	{
		return CPoint(0, 0);
	}

	const CPoint cell((point.x - kFrameOffset) / m_cellSize + 1, (point.y - kFrameOffset) / m_cellSize + 1);
	return CBoard::IsInside(cell) ? cell : CPoint(0, 0);
}
