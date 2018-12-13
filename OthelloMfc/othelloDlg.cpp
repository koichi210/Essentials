// othelloDlg.cpp : インプリメンテーション ファイル
//

#include "stdafx.h"
#include "othelloDef.h"
#include "othellobase.h"
#include "manager.h"
#include "com.h"
#include "othello.h"
#include "othelloDlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#undef THIS_FILE
static char THIS_FILE[] = __FILE__;
#endif

/////////////////////////////////////////////////////////////////////////////
// アプリケーションのバージョン情報で使われている CAboutDlg ダイアログ

class CAboutDlg : public CDialog
{
public:
	CAboutDlg();

// ダイアログ データ
	//{{AFX_DATA(CAboutDlg)
	enum { IDD = IDD_ABOUTBOX };
	//}}AFX_DATA

	// ClassWizard は仮想関数のオーバーライドを生成します
	//{{AFX_VIRTUAL(CAboutDlg)
	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV のサポート
	//}}AFX_VIRTUAL

// インプリメンテーション
protected:
	//{{AFX_MSG(CAboutDlg)
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()
};

CAboutDlg::CAboutDlg() : CDialog(CAboutDlg::IDD)
{
	//{{AFX_DATA_INIT(CAboutDlg)
	//}}AFX_DATA_INIT
}

void CAboutDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	//{{AFX_DATA_MAP(CAboutDlg)
	//}}AFX_DATA_MAP
}

BEGIN_MESSAGE_MAP(CAboutDlg, CDialog)
	//{{AFX_MSG_MAP(CAboutDlg)
		// メッセージ ハンドラがありません。
	//}}AFX_MSG_MAP
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// COthelloDlg ダイアログ

// construct
COthelloDlg::COthelloDlg(CWnd* pParent /*=NULL*/)
	: CDialog(COthelloDlg::IDD, pParent)
{
	//{{AFX_DATA_INIT(COthelloDlg)
		// メモ: この位置に ClassWizard によってメンバの初期化が追加されます。
	//}}AFX_DATA_INIT
	// メモ: LoadIcon は Win32 の DestroyIcon のサブシーケンスを要求しません。
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void COthelloDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialog::DoDataExchange(pDX);
	//{{AFX_DATA_MAP(COthelloDlg)
		// メモ: この場所には ClassWizard によって DDX と DDV の呼び出しが追加されます。
	//}}AFX_DATA_MAP
}

BEGIN_MESSAGE_MAP(COthelloDlg, CDialog)
	//{{AFX_MSG_MAP(COthelloDlg)
	ON_WM_SYSCOMMAND()
	ON_WM_QUERYDRAGICON()
	ON_WM_PAINT()
	ON_WM_DESTROY()
	ON_WM_GETMINMAXINFO()
	ON_WM_SIZE()
	ON_WM_TIMER()
	ON_WM_LBUTTONDOWN()
	ON_WM_LBUTTONUP()
	ON_WM_RBUTTONDOWN()
	ON_COMMAND(ID_MENUITEM_START, OnMenuitemStart)
	ON_COMMAND(ID_MENUITEM_STOP, OnMenuitemStop)
	ON_COMMAND(ID_MENUITEM_EXIT, OnMenuitemExit)
	ON_COMMAND(ID_MENUITEM_RESET, OnMenuitemReset)
	ON_COMMAND(ID_MENUITEM_PLAYMD_PP, OnMenuitemPlaymdPp)
	ON_COMMAND(ID_MENUITEM_PLAYMD_PC, OnMenuitemPlaymdPc)
	ON_COMMAND(ID_MENUITEM_PLAYMD_CP, OnMenuitemPlaymdCp)
	ON_COMMAND(ID_MENUITEM_PLAYMD_CC, OnMenuitemPlaymdCc)
	ON_COMMAND(ID_MENUITEM_ComLevel1, OnMenuitemComLevel1)
	ON_COMMAND(ID_MENUITEM_ComLevel2, OnMenuitemComLevel2)
	ON_COMMAND(ID_MENUITEM_ComLevel3, OnMenuitemComLevel3)
	ON_COMMAND(ID_MENUITEM_TIME_05, OnMenuitemTime0)
	ON_COMMAND(ID_MENUITEM_TIME_1, OnMenuitemTime1)
	ON_COMMAND(ID_MENUITEM_TIME_2, OnMenuitemTime2)
	ON_COMMAND(ID_MENUITEM_TIME_3, OnMenuitemTime3)
	ON_COMMAND(ID_MENUITEM_TIME_5, OnMenuitemTime5)
	ON_COMMAND(ID_MENUITEM_TIME_10, OnMenuitemTime10)
	ON_COMMAND(ID_MENUITEM_TIME_15, OnMenuitemTime15)
	ON_COMMAND(ID_MENUITEM_KIHU_SHOW, OnMenuitemKihuShow)
	ON_COMMAND(ID_MENUITEM_KIHU_SAVE, OnMenuitemKihuSave)
	ON_COMMAND(ID_MENUITEM_VERS, OnMenuitemVers)
	ON_COMMAND(ID_MENUITEM_REVERS, OnMenuitemRevers)
	ON_COMMAND(ID_MENUITEM_Version, OnMenuitemVersion)
	ON_COMMAND(ID_MENUITEM_HOWTOPLAY, OnMenuitemHowtoplay)
	ON_COMMAND(ID_MENUITEM_KIHU_READ, OnMenuitemKihuRead)
	ON_COMMAND(ID_MENUITEM_TIME_NONE, OnMenuitemTimeNone)
	ON_COMMAND(ID_MENUITEM_PUT_NOTICE, OnMenuitemPutNotice)
	//}}AFX_MSG_MAP
END_MESSAGE_MAP()

/////////////////////////////////////////////////////////////////////////////
// COthelloDlg メッセージ ハンドラ

BOOL COthelloDlg::OnInitDialog()
{
	CDialog::OnInitDialog();

	// "バージョン情報..." メニュー項目をシステム メニューへ追加します。

	// IDM_ABOUTBOX はコマンド メニューの範囲でなければなりません。
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);

	CMenu* pSysMenu = GetSystemMenu(FALSE);
	if (pSysMenu != NULL)
	{
		CString strAboutMenu;
		strAboutMenu.LoadString(IDS_ABOUTBOX);
		if (!strAboutMenu.IsEmpty())
		{
			pSysMenu->AppendMenu(MF_SEPARATOR);
			pSysMenu->AppendMenu(MF_STRING, IDM_ABOUTBOX, strAboutMenu);
		}
	}

	// このダイアログ用のアイコンを設定します。フレームワークはアプリケーションのメイン
	// ウィンドウがダイアログでない時は自動的に設定しません。
	SetIcon(m_hIcon, TRUE);			// 大きいアイコンを設定
	SetIcon(m_hIcon, FALSE);		// 小さいアイコンを設定
	
	// instance 2 class
	manager = new CManager ;
	com = new CComProc ;

	// Initialize
	AppInit() ;

	return TRUE;  // TRUE を返すとコントロールに設定したフォーカスは失われません。
}

// +-+-+-+- resize 2 dialog +-+-+-+-+-
void COthelloDlg::OnSize(UINT nType, int cx, int cy) 
{
	// update Rect
	UpdateRect() ;

	// refresh 2 picture
	Invalidate();

	CDialog::OnSize(nType, cx, cy);
}

// +-+-+-+- after proc 2 application +-+-+-+-+-
void COthelloDlg::OnDestroy() 
{
	CDialog::OnDestroy();
	
	delete manager ;
	delete com ;
}

//ウィンドウの左上をクリックしたとき
void COthelloDlg::OnSysCommand(UINT nID, LPARAM lParam)
{
	if ((nID & 0xFFF0) == IDM_ABOUTBOX)
	{
		CAboutDlg dlgAbout;
		dlgAbout.DoModal();
	}
	else
	{
		CDialog::OnSysCommand(nID, lParam);
	}
}

// システムは、ユーザーが最小化ウィンドウをドラッグしている間、
// カーソルを表示するためにここを呼び出します。
HCURSOR COthelloDlg::OnQueryDragIcon()
{
	return (HCURSOR) m_hIcon;
}

// +-+-+-+- clicked minimum or maxmimum on dialog +-+-+-+-+-
void COthelloDlg::OnGetMinMaxInfo(MINMAXINFO FAR* lpMMI)
{
	if(lpMMI->ptMinTrackSize.x < (WIN_MIN_SIZE + TEXT_OFFSET)){
		lpMMI->ptMinTrackSize.x = (WIN_MIN_SIZE + TEXT_OFFSET) ;
	}

	if(lpMMI->ptMinTrackSize.y < WIN_MIN_SIZE){
		lpMMI->ptMinTrackSize.y = WIN_MIN_SIZE ;
	}
	CDialog::OnGetMinMaxInfo(lpMMI);
}

// もしダイアログボックスに最小化ボタンを追加するならば、アイコンを描画する
// コードを以下に記述する必要があります。MFC アプリケーションは document/view
// モデルを使っているので、この処理はフレームワークにより自動的に処理されます。
// ダイアログ再描画
void COthelloDlg::OnPaint() 
{
	CPaintDC dc(this); // device context from draw
	int turn ;

	if (IsIconic())
	{
		SendMessage(WM_ICONERASEBKGND, (WPARAM) dc.GetSafeHdc(), 0);

		// center of rect area on client
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// draw icon
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		// 表示位置修正 (ホントはOnSizeの中でやりたいけど、初期起動時はGetRectできないので。。）
		// update position
		UpdatePos() ;

		// reminder time limit
		SetTimerString() ;

		// draw table
		DrawTable(&dc) ;

		// notice put stone
		if(manager->put_notice == ON){
			turn = manager->GetTurnNow(manager->play_mode, manager->color) ;
			DrawNotice(&dc, turn) ;
		}

		CDialog::OnPaint();
	}
}

// +-+-+-+- draw table +-+-+-+-+-
// CDC *pDC		device context from draw
void COthelloDlg::DrawTable(CDC * pDC)
{
	CPoint z ;				// line-X and line-Y
	CPen * oldpen ;			// before pen
	CPen pen ;				// use pen
	char PaintTable[LINE_MAX][LINE_MAX] ;
	CRect rect ;
	CBrush bg ;			// before brush
	CBrush * oldbrush ;		// before brush

	// creat brush from paint out
	bg.CreateSolidBrush(RGB(60, 150, 60)) ;
	oldbrush = pDC->SelectObject(&bg);

	// draw background
	rect.top = FRAME_OFFSET ;
	rect.left = FRAME_OFFSET ;
	rect.bottom = LINE_MAX * cellsize + FRAME_OFFSET;
	rect.right = LINE_MAX * cellsize + FRAME_OFFSET;
	pDC->Rectangle(rect);

	// return brush
	pDC->SelectObject(oldbrush);


	// creat pen from draw
	pen.CreatePen(PS_SOLID, 1, RGB(0, 0, 0)) ;

	// save 2 pen return from device context
	oldpen = pDC->SelectObject(&pen) ;

	// draw length line
	for(z.x=0; z.x<=LINE_MAX; z.x++){
		pDC->MoveTo(z.x * cellsize + FRAME_OFFSET, FRAME_OFFSET);		// ここにペンを置いて
		pDC->LineTo(z.x * cellsize + FRAME_OFFSET, LINE_MAX * cellsize + FRAME_OFFSET) ;	// ここまで書く
	}
	// width line
	for(z.y=0; z.y<=LINE_MAX; z.y++){
		pDC->MoveTo(FRAME_OFFSET, z.y * cellsize + FRAME_OFFSET);					// ここにペンを置いて
		pDC->LineTo(LINE_MAX * cellsize + FRAME_OFFSET,z.y * cellsize + FRAME_OFFSET) ;	// ここまで書く
	}

	// return pen
	pDC->SelectObject(oldpen) ;

	// delete pen
	pen.DeleteObject() ;

	// get table
	manager->GetTable(PaintTable) ;

	// draw stone and cell
	for(z.y=0; z.y<=LINE_MAX; z.y++){
		for(z.x=0; z.x<=LINE_MAX; z.x++){
			if(z.y == 0 || z.x == 0){
				DrawNumber(pDC, z) ;
			}else{
				if(PaintTable[z.y-1][z.x-1] == BLACK ||
					PaintTable[z.y-1][z.x-1] == WHITE){
					DrawStone(pDC, z, PaintTable[z.y-1][z.x-1]) ;
				}
			}
		}
	}
}

// +-+-+-+- draw cell number +-+-+-+-+-
// CPoint	z	matrix of draw number
void COthelloDlg::DrawNumber(CDC * pDC, CPoint z)
{
	int old_BkMode ;
	char str[STR_BUFF] ;
	CPoint str_pos ;

	::memset(&str, 0, sizeof(str)) ;
	old_BkMode = pDC->SetBkMode(TRANSPARENT) ;

	str_pos.x = z.x * cellsize ;
	str_pos.y = z.y * cellsize ;

	// 4 とか 9 は微調整のため可変だなや。。
	// 4 and 9 is flacture that need ajust
	if(z.x != 0 || z.y != 0){
		if(z.x != 0){
			str_pos.x += FRAME_OFFSET - (cellsize / 2 +4) ;
			str_pos.y += NUM_OFFSET ;
			sprintf(str, "%c",0x40+z.x) ;	// A to H
		}else{
			str_pos.x += NUM_OFFSET ;
			str_pos.y += FRAME_OFFSET - (cellsize / 2 +9) ;
			sprintf(str, "%d",z.y) ;			// 1 to 8
		}
		pDC->TextOut(str_pos.x, str_pos.y, str) ;
	}
	pDC->SetBkMode(old_BkMode) ;
}

// +-+-+-+- draw stone +-+-+-+-+-
// CPoint	z	matrix of draw stone
// int		clr	color of draw stone
void COthelloDlg::DrawStone(CDC * pDC, CPoint z, int clr)
{
	CRect rect ;
	CSize	circle ;
	CBrush black, white ;	// paint out color
	CBrush * oldbrush ;		// before brush

	// creat brush from paint out
	black.CreateSolidBrush(RGB(0, 0, 0)) ;
	white.CreateSolidBrush(RGB(255, 255, 255)) ;

	// get matrix(X,Y) from draw stone
	circle.cx = (z.x-1) * cellsize ;
	circle.cy = (z.y-1) * cellsize ;

	// set rect from draw circle
	rect.left	= (z.x-1) * cellsize + cell_offset + FRAME_OFFSET ;
	rect.right	=   z.x   * cellsize - cell_offset + FRAME_OFFSET ;
	rect.top	= (z.y-1) * cellsize + cell_offset + FRAME_OFFSET ;
	rect.bottom	=   z.y   * cellsize - cell_offset + FRAME_OFFSET ;

	// select brush and return value is orginal brush
	if(clr == BLACK){
		oldbrush = pDC->SelectObject(&black);
	}else{
		oldbrush = pDC->SelectObject(&white);
	}

	// draw circle Ellipse(x1,y1,x2,y2)
	pDC->Ellipse(&rect) ;

	// return brush
	pDC->SelectObject(oldbrush);

	// delete brush
	black.DeleteObject() ;
	white.DeleteObject() ;
}

// +-+-+-+- draw small circle +-+-+-+-+-
// CPoint	z	matrix from draw stone
void COthelloDlg::DrawNotice(CDC * pDC, int turn)
{
	int mass_center = cellsize / 2 ;
	CRect rect ;
	CClientDC dc(this) ;
	CSize	circle ;
	CBrush black ;		// paint out color
	CBrush * oldbrush ;	// before brush
	CPoint  put_z[LINE_MAX*LINE_MAX] ;
	CPoint z ;
	int cnt = 0 ;

	::memset(put_z, 0, sizeof(put_z)) ;

	// create brush from paint out
	if(turn == PLAYER){
		black.CreateSolidBrush(RGB(0, 0, 255)) ;
	}else{
		black.CreateSolidBrush(RGB(255, 0, 0)) ;
	}
	// select brush and retrun value is orginal brush
	oldbrush = pDC->SelectObject(&black);

	manager->GetPutNotice(manager->color, put_z) ;
	for(cnt=0; put_z[cnt].x != 0; cnt++){
		::memcpy(&z, &put_z[cnt], sizeof(z)) ;
		// get matrix(X,Y) from draw stone
		circle.cx = (z.x-1) * cellsize ;
		circle.cy = (z.y-1) * cellsize ;

		// set rect from draw circle
		rect.left	= (z.x-1) * cellsize + mass_center - 2 + FRAME_OFFSET ;
		rect.right	= rect.left + 4 ;
		rect.top	= (z.y-1) * cellsize + mass_center - 2 + FRAME_OFFSET ;
		rect.bottom	= rect.top + 4 ;

		// draw circle Ellipse(x1,y1,x2,y2)
		pDC->Ellipse(&rect) ;
	}

	// return brush
	pDC->SelectObject(oldbrush);

	// delete brush
	black.DeleteObject() ;
}

//////////  ほぼ既存処理（Windowsイベント関係） ↑  ///////////
//////////  almost existing proc(Windows event relation）↑  ///////////

// *****************  MENU  ******************* 
// +-+-+-+- reset from toolbar +-+-+-+-+-
void COthelloDlg::OnMenuitemReset() 
{
	// stop 2 timer
	this->KillTimer(BLACK_TIMER_ID);
	this->KillTimer(WHITE_TIMER_ID);
	this->KillTimer(COM_TIMER_ID);

	// cancel constraint
	Constraints(FALSE) ;

	// picture init
	GameInit() ;
}

// +-+-+-+- start from toolbar +-+-+-+-+-
// start from once stop....ex)start timer
void COthelloDlg::OnMenuitemStart() 
{
	// when start keep procedure in game then start COM timer 
	StartProc() ;
	Invalidate();
}

// +-+-+-+- stop from toolbar +-+-+-+-+-
void COthelloDlg::OnMenuitemStop() 
{
	// stop timer
	this->KillTimer(BLACK_TIMER_ID);
	this->KillTimer(WHITE_TIMER_ID);
	this->KillTimer(COM_TIMER_ID);

	if(manager->game_state != GAME_END){
		manager->UpDateGameState(GAME_STOP) ;
		SetMenuStatus(ENABLE, ID_MENUITEM_START, MF_ENABLED) ;
		SetMenuStatus(ENABLE, ID_MENUITEM_STOP, MF_GRAYED) ;
	}
}

// +-+-+-+- exit from toolbar +-+-+-+-+-
void COthelloDlg::OnMenuitemExit() 
{
	OnCancel() ;
}

// +-+-+-+- Put Location Notice +-+-+-+-+-
void COthelloDlg::OnMenuitemPutNotice() 
{
	if(manager->put_notice == ON){
		manager->put_notice = OFF;
		SetMenuStatus(CHECK, ID_MENUITEM_PUT_NOTICE, MF_UNCHECKED) ;
	}else{
		manager->put_notice = ON;
		SetMenuStatus(CHECK, ID_MENUITEM_PUT_NOTICE, MF_CHECKED) ;
	}
	Invalidate();
}


// +-+-+-+- PLAYER VS PLAYER +-+-+-+-+-
void COthelloDlg::OnMenuitemPlaymdPp() 
{
	manager->play_mode = PLAYER_PLAYER ;
	manager->com_interval = VS_MODE_INTERVAL ;
	SetMenuPlayMode(ID_MENUITEM_PLAYMD_PP) ;
}

// +-+-+-+- PLAYER VS COM +-+-+-+-+-
void COthelloDlg::OnMenuitemPlaymdPc() 
{
	manager->play_mode = PLAYER_COM ;
	manager->com_interval = VS_MODE_INTERVAL ;
	SetMenuPlayMode(ID_MENUITEM_PLAYMD_PC) ;
}

// +-+-+-+- COM VS PLAYER +-+-+-+-+-
void COthelloDlg::OnMenuitemPlaymdCp() 
{
	manager->play_mode = COM_PLAYER ;
	manager->com_interval = VS_MODE_INTERVAL ;
	SetMenuPlayMode(ID_MENUITEM_PLAYMD_CP) ;
}

// +-+-+-+- COM VS COM +-+-+-+-+-
void COthelloDlg::OnMenuitemPlaymdCc() 
{
	manager->play_mode = COM_COM ;
	manager->com_interval = COM_MODE_INTERVAL ;
	SetMenuPlayMode(ID_MENUITEM_PLAYMD_CC) ;
}

// +-+-+-+- check state from select play mode +-+-+-+-+-
void COthelloDlg::SetMenuPlayMode(int id)
{
	// check
	SetMenuStatus(CHECK, ID_MENUITEM_PLAYMD_PP, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, ID_MENUITEM_PLAYMD_PC, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, ID_MENUITEM_PLAYMD_CP, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, ID_MENUITEM_PLAYMD_CC, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, id, MF_CHECKED) ;
}

// +-+-+-+- COM LEVEL 1 +-+-+-+-+-
void COthelloDlg::OnMenuitemComLevel1()
{
	manager->com_level = 1 ;
	SetMenuComLevel(ID_MENUITEM_ComLevel1) ;
}

// +-+-+-+- COM LEVEL 2 +-+-+-+-+-
void COthelloDlg::OnMenuitemComLevel2() 
{
	manager->com_level = 2 ;
	SetMenuComLevel(ID_MENUITEM_ComLevel2) ;
}

// +-+-+-+- COM LEVEL 3 +-+-+-+-+-
void COthelloDlg::OnMenuitemComLevel3() 
{
	manager->com_level = 3 ;
	SetMenuComLevel(ID_MENUITEM_ComLevel3) ;
}

// +-+-+-+- check stat from select COM level +-+-+-+-+-
void COthelloDlg::SetMenuComLevel(int id)
{
	// check
	SetMenuStatus(CHECK, ID_MENUITEM_ComLevel1, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, ID_MENUITEM_ComLevel2, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, ID_MENUITEM_ComLevel3, MF_UNCHECKED) ;
	SetMenuStatus(CHECK, id, MF_CHECKED) ;
}

// +-+-+-+- time limit is none +-+-+-+-+-
void COthelloDlg::OnMenuitemTimeNone() 
{
	SetMenuTime(ID_MENUITEM_TIME_NONE, -1) ;
}

// +-+-+-+- time limit is 30(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime0() 
{
	SetMenuTime(ID_MENUITEM_TIME_05, 30) ;
}

// +-+-+-+- time limit is 60(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime1() 
{
	SetMenuTime(ID_MENUITEM_TIME_1, 60) ;
}

// +-+-+-+- time limit is 120(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime2() 
{
	SetMenuTime(ID_MENUITEM_TIME_2, 120) ;
}

// +-+-+-+- time limit is 180(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime3() 
{
	SetMenuTime(ID_MENUITEM_TIME_3, 180) ;
}

// +-+-+-+- time limit is 300(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime5() 
{
	SetMenuTime(ID_MENUITEM_TIME_5, 300) ;
}

// +-+-+-+- time limit is 600(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime10() 
{
	SetMenuTime(ID_MENUITEM_TIME_10, 600) ;
}

// +-+-+-+- time limit is 900(sec) +-+-+-+-+-
void COthelloDlg::OnMenuitemTime15() 
{
	SetMenuTime(ID_MENUITEM_TIME_15, 900) ;
}

// +-+-+-+- check state from select time limit +-+-+-+-+-
void COthelloDlg::SetMenuTime(int id, int time)
{
	// set timer black and white
	time_limit = time;
	black_time = time_limit * 1000 ;
	white_time = time_limit * 1000;
	
	// uncheck all ID and check select ID
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_NONE, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_05, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_1, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_2, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_3, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_5, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_10, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_TIME_15, MF_UNCHECKED ) ;
	SetMenuStatus(CHECK, id, MF_CHECKED) ;

	// show or hide
	if(id == ID_MENUITEM_TIME_NONE){
		GetDlgItem(IDC_BLACK_TIME)->ShowWindow(FALSE) ;
		GetDlgItem(IDC_WHITE_TIME)->ShowWindow(FALSE) ;
		GetDlgItem(IDC_BLACK_TIME_NAME)->ShowWindow(FALSE) ;
		GetDlgItem(IDC_WHITE_TIME_NAME)->ShowWindow(FALSE) ;
	}else{
		GetDlgItem(IDC_BLACK_TIME)->ShowWindow(TRUE) ;
		GetDlgItem(IDC_WHITE_TIME)->ShowWindow(TRUE) ;
		GetDlgItem(IDC_BLACK_TIME_NAME)->ShowWindow(TRUE) ;
		GetDlgItem(IDC_WHITE_TIME_NAME)->ShowWindow(TRUE) ;
	}

	SetTimerString() ;
}

// +-+-+-+- show KIHU +-+-+-+-+-
void COthelloDlg::OnMenuitemKihuShow() 
{
	manager->GetKihu(SHOW, "") ;
}

// +-+-+-+- save KIHU +-+-+-+-+-
void COthelloDlg::OnMenuitemKihuSave() 
{
	CString path ;
#if ENGLISH
	CFileDialog fd(FALSE, "*.rkf", NULL, OFN_HIDEREADONLY | OFN_OVERWRITEPROMPT, "Othello kifu Files (*.txt)|*.txt|All Files (*.*)|*.*||");
#else
	CFileDialog fd(FALSE, "*.rkf", NULL, OFN_HIDEREADONLY | OFN_OVERWRITEPROMPT, "オセロ棋譜ファイル (*.rkf)|*.rkf|All Files (*.*)|*.*||");
#endif
	if (fd.DoModal() == IDOK){
		path = fd.GetPathName();
		manager->GetKihu(SAVE, path) ;
	}
}

// +-+-+-+- vers +-+-+-+-+-
void COthelloDlg::OnMenuitemVers() 
{
	if(manager->game_state == GAME_PLAY || manager->game_state == GAME_STOP)
	{
		OnMenuitemStop();
		VersProc() ;
		ConstraintsOperator();
		Invalidate();
	}
}

// +-+-+-+- revers +-+-+-+-+-
void COthelloDlg::OnMenuitemRevers() 
{
	if(manager->game_state == GAME_PLAY || manager->game_state == GAME_STOP)
	{
		OnMenuitemStop();
		ReVersProc() ;
		ConstraintsOperator();
		Invalidate();
	}
}

// +-+-+-+- varsion infomation +-+-+-+-+-
void COthelloDlg::OnMenuitemVersion() 
{
	CAboutDlg dlgAbout;
	dlgAbout.DoModal();
}

// +-+-+-+- how to play othello +-+-+-+-+-
void COthelloDlg::OnMenuitemHowtoplay() 
{
	char str[STR_BUFF] ;
#if ENGLISH
	sprintf(str, "this is normal reversi(｡-_-｡)") ;
#else
	sprintf(str, "普通のオセロです(｡-_-｡)") ;
#endif
	AfxMessageBox(str) ;
	
}

// +-+-+-+- set Disable  dont touch playing game +-+-+-+-+-
void COthelloDlg::Constraints(BOOL flg) 
{
	int state ;

	if(flg == TRUE){
		// constraint ON
		state = MF_GRAYED ;
	}else{
		// constraint OFF
		state = MF_ENABLED ;
	}

	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_NONE, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_05, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_1, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_2, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_3, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_5, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_10, state) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_TIME_15, state) ;
}

// +-+-+-+- set Disable  Operator +-+-+-+-+-
void COthelloDlg::ConstraintsOperator() 
{
	if(manager->turn_cnt == manager->turning_point){
		SetMenuStatus(ENABLE, ID_MENUITEM_VERS, MF_GRAYED) ;
	}else{
		SetMenuStatus(ENABLE, ID_MENUITEM_VERS, MF_ENABLED) ;
	}

	if(manager->turn_cnt > 0){
		SetMenuStatus(ENABLE, ID_MENUITEM_REVERS, MF_ENABLED) ;
	}else{
		SetMenuStatus(ENABLE, ID_MENUITEM_REVERS, MF_GRAYED) ;
	}
}

// +-+-+-+- left button down +-+-+-+-+-
void COthelloDlg::OnLButtonDown(UINT nFlags, CPoint point) 
{
	CPoint click ;

	click.x = (point.x - FRAME_OFFSET) / cellsize + 1 ;
	click.y = (point.y - FRAME_OFFSET) / cellsize + 1 ;

	// if clicked matrix is cell then memories cell
	if( (click.x >= LINE_MIN) &&
		(click.x <= LINE_MAX) &&
		(click.y >= LINE_MIN) &&
		(click.y <= LINE_MAX) ){
		ldown.x = click.x ;
		ldown.y = click.y ;
	}else{
		ldown.x = 0 ;
		ldown.y = 0 ;
	}

	CDialog::OnLButtonDown(nFlags, point);
}

// +-+-+-+- left button up +-+-+-+-+-
void COthelloDlg::OnLButtonUp(UINT nFlags, CPoint point) 
{
	CPoint click ;		// clicked matrix
	int turn_rt = 0 ;	// which turn?
	int put_rt = 0 ;	// is put?
	int com_rt = 0 ;	// return 2 COM proc

	click.x = (point.x - FRAME_OFFSET) / cellsize + 1 ;
	click.y = (point.y - FRAME_OFFSET) / cellsize + 1 ;

	if(manager->game_state != GAME_END){
		turn_rt = manager->GetTurnNow(manager->play_mode, manager->color) ;
		if(turn_rt == PLAYER){
			// the identical matrix of LDOWN
			if( (click.x == ldown.x) &&
				(click.y == ldown.y) ){
				put_rt = manager->PutCheck(click, manager->color, CHK_ON, NULL) ;

				// is stone put?
				if(put_rt == PUT_NORMAL){
					if(manager->game_state == GAME_STOP){
						AfxMessageBox("pls click [START] button on [GAME]") ;
					}else{
						// when start dont keep procedure in game then start COM timer 
						if(manager->game_state == GAME_INIT){
							StartProc() ;
						}
						OthelloProc(click) ;
						ConstraintsOperator();
					}
				}
			}
		}
	}

	CDialog::OnLButtonUp(nFlags, point);
}

// +-+-+-+- right button down(most from debug) +-+-+-+-+-
void COthelloDlg::OnRButtonDown(UINT nFlags, CPoint point) 
{
}

// +-+-+-+- push OK button +-+-+-+-+-
void COthelloDlg::OnOK() 
{

//	CDialog::OnOK();
}

// +-+-+-+- push Cancel button +-+-+-+-+-
void COthelloDlg::OnCancel() 
{
	CDialog::OnCancel();
}

// +-+-+- initial dialog +-+-+-
void COthelloDlg::GameInit() 
{
	CString str ;

	// initial state
	manager->GameValueInit() ;

	// initial title bar
	str.Format("othello");
	SetWindowText(str);

	// initial timer
	black_time = time_limit * 1000 ;
	white_time = time_limit * 1000 ;

	// set menu bar(start,stop)
	SetMenuStatus(ENABLE, ID_MENUITEM_START, MF_ENABLED) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_STOP, MF_GRAYED) ;

	// refresh picture
	Invalidate() ;

	// update score
	UpdateScore() ;
}

// +-+-+-+- othello main proc +-+-+-+-+-
// return == NORMAL_RETURN	put stone normal
// return != NORMAL_RETURN	put stone illegal
// CPoint	z				matrix of put stone(IN)
int COthelloDlg::OthelloProc(CPoint z)
{
	int change_rt ;
	int put_rt ;
	int put_stone[ORIENT_MAX] ;

	CPaintDC dc(this); // device context from draw

	::memset(put_stone, 0, sizeof(put_stone)) ;

	put_rt = manager->PutCheck(z, manager->color, PUT_ON, put_stone) ;
	if(put_rt != PUT_NORMAL){
		if(manager->play_mode != COM_COM){
#if ENGLISH
			AfxMessageBox("matrix put stone is illegal") ;
#else
			AfxMessageBox("置くところ不正") ;
#endif
		}
		return put_rt ;
	}

	// draw stone
	manager->PutStone(z, put_stone, manager->color) ;
	DrawTable(&dc) ;
	Invalidate() ;

	// save KIHU
	manager->UpDateKihu(z, put_stone) ;

	// judge change turn
	change_rt = manager->PutCheckWhich(manager->color) ;
	if(change_rt == WHITE || change_rt == BLACK){
		ChangeColor(change_rt) ;

		// update score
		UpdateScore() ;
	}else if(change_rt == GAME_END){
		// game end
		EndProc(NULL) ;
	}

	return NORMAL_RETURN ;
}

// +-+-+-+- start proc +-+-+-+-+-
void COthelloDlg::StartProc() 
{
	int id ;
	
	if(manager->game_state == GAME_END){
		GameInit() ;
	}

	// UpDate game state
	manager->UpDateGameState(GAME_PLAY) ;

	// start timer white or black ID
	if(manager->color == BLACK){
		id = BLACK_TIMER_ID ;
	}else{
		id = WHITE_TIMER_ID ;
	}

	// start timer white or black, and com
	if(time_limit != -1){
		this->SetTimer(id, COUNT_DOWN_CYC, NULL);
	}
	this->SetTimer(COM_TIMER_ID, manager->com_interval, NULL);

	// Disable start button, and Enable stop button
	SetMenuStatus(ENABLE, ID_MENUITEM_START, MF_GRAYED) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_STOP, MF_ENABLED) ;

	// set constraint
	Constraints(TRUE) ;
}

// +-+-+-+- end proc +-+-+-+-+-
void COthelloDlg::EndProc(int state) 
{
	int black_num ;
	int white_num ;
	int to_clr ;
	char str[STR_BUFF] ;
	CString title_str;

	::memset(&str, 0, sizeof(str)) ;

	// UpDate game state
	manager->UpDateGameState(GAME_END) ;

	// cancel timer
	this->KillTimer(BLACK_TIMER_ID);
	this->KillTimer(WHITE_TIMER_ID);
	this->KillTimer(COM_TIMER_ID);

	// Enable start button, and Disable stop button
	SetMenuStatus(ENABLE, ID_MENUITEM_START, MF_ENABLED) ;
	SetMenuStatus(ENABLE, ID_MENUITEM_STOP, MF_GRAYED) ;

	// cancel constraint
	Constraints(FALSE) ;

	// view result
	UpdateScore() ;

	if(state == TIME_OUT){

		// notice time out
#if ENGLISH
		sprintf(str, "time out\n") ;
#else
		sprintf(str, "タイムアウト\n") ;
#endif
		if(black_time <= 0){
			to_clr = BLACK ;
		}else{
			to_clr = WHITE ;
		}
		manager->GetColorName(to_clr, str) ;
#if ENGLISH
		strcat(str, "is lose. and....") ;
#else
		strcat(str, "の負けです。\n\nちなみに・・・") ;
#endif
		AfxMessageBox(str) ;
	}

	// calculation sutone number
	manager->CntStone(&black_num, &white_num) ;

	// view result
#if ENGLISH
	sprintf(str, "black:%d  white:%d\n game set",black_num,white_num) ;
#else
	sprintf(str, "黒：%d　白：%d\n ゲーム終了",black_num,white_num) ;
#endif
	AfxMessageBox(str) ;

	// view title bar
#if ENGLISH
	title_str.Format("othello  end");
#else
	title_str.Format("othello  終了");
#endif
	SetWindowText(title_str);
}

// +-+-+-+- COM turn proc +-+-+-+-+-
// return == COM_NORMAL		COM is put stone and normal end
// return != COM_NORMAL		if not COM turn or matrix of put com stone is illeagal
int COthelloDlg::ComRoutin()
{
	CPoint com_put ;
	int rt = COM_ERROR ;
	int turn_rt ;
	char str[STR_BUFF] ;

	turn_rt = manager->GetTurnNow(manager->play_mode, manager->color) ;
	if(turn_rt == COM){
		// COM turn
		manager->GetTable(com->Table) ;
		rt = com->Turn(&com_put, manager->color, manager->com_level, manager->turn_cnt) ;
		if(rt != COM_NORMAL){
			this->KillTimer(COM_TIMER_ID);
			::memset(str, 0, STR_BUFF) ;
#if ENGLISH
			sprintf(str, "COM is illegal(x=%d y=%d)",com_put.x,com_put.y) ;
#else
			sprintf(str, "COM 置くところ不正(x=%d y=%d)",com_put.x,com_put.y) ;
#endif
			AfxMessageBox(str) ;
		}else{
			// found matrix of put stone
			rt = OthelloProc(com_put) ;
		}
	}

	return rt ;
}

// +-+-+-+- timer event +-+-+-+-+-
void COthelloDlg::OnTimer(UINT nIDEvent) 
{
//	CString tl_str ;	// time limit 時間数
	int time ;
	int id ;

	if(nIDEvent == COM_TIMER_ID){
		ComRoutin() ;
	}else if(nIDEvent == BLACK_TIMER_ID || nIDEvent == WHITE_TIMER_ID){
		if(nIDEvent == BLACK_TIMER_ID){
			black_time -= 100 ;
			time = black_time / 1000 ;
			id = IDC_BLACK_TIME ;
		}else{
			white_time -= 100 ;
			time = white_time / 1000 ;
			id = IDC_WHITE_TIME ;
		}

		// set timer black and white
		SetTimerString() ;

		if(black_time <= 0 ||
			white_time <= 0){
			TimeOutProc() ;
		}
	}
	CDialog::OnTimer(nIDEvent);
}

// +-+-+- time out proc +-+-+-
void COthelloDlg::TimeOutProc()
{
	EndProc(TIME_OUT) ;
}

// +-+-+- count down timer +-+-+-
// int	attrib(IN)	= BLACK		set black timer
//					= WHITE		set white timer
//					= GAME_END	stop timer
void COthelloDlg::SetCountDownTimer(int attrib)
{
	int enemy_ID ;
	int my_ID ;

	if(attrib == BLACK || attrib ==WHITE){
		if(attrib == BLACK){
			my_ID = BLACK_TIMER_ID ;
			enemy_ID = WHITE_TIMER_ID ;
		}else{
			my_ID = WHITE_TIMER_ID ;
			enemy_ID = BLACK_TIMER_ID ;
		}

		// cancel enemy timer and set my timer start 100ms
		if(time_limit != -1){
			this->KillTimer(enemy_ID) ;
			this->SetTimer(my_ID, COUNT_DOWN_CYC, NULL) ;
		}

	}else if(attrib == GAME_END){
		// stop timer
		if(time_limit != -1){
			this->KillTimer(BLACK_TIMER_ID) ;
			this->KillTimer(WHITE_TIMER_ID) ;
		}
	}
}

// +-+-+-+- view remmainder time +-+-+-+-+-
void COthelloDlg::SetTimerString()
{
	int time ;
	int min ;
	int sec ;
	CString str ;

	// set black timer
	time = black_time / 1000 ;
	min = time / 60 ;
	sec = time % 60 ;
	str.Format("%02d 分 %02d 秒",min,sec) ;
	GetDlgItem(IDC_BLACK_TIME)->SetWindowText(str);

	// set white timer
	time = white_time / 1000 ;
	min = time / 60 ;
	sec = time % 60 ;
		
	str.Format("%02d 分 %02d 秒",min,sec) ;
	GetDlgItem(IDC_WHITE_TIME)->SetWindowText(str);
}

// +-+-+-+- adjust view possition +-+-+-+-+-
void COthelloDlg::UpdatePos()
{
	CClientDC dc(this) ;
	CRect rect ;
	CPoint wk_pos ;

	// init
	::memcpy(&wk_pos, &time_pos, sizeof(CPoint)) ;

	// set turn name posstion
	GetDlgItem(IDC_TURN_TEXT)->GetClientRect(&rect) ;
	GetDlgItem(IDC_TURN_TEXT)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;

	// set score possition
	wk_pos.y += TIME2TEXT_OFFSET ;
	GetDlgItem(IDC_SCORE)->GetClientRect(&rect) ;
	GetDlgItem(IDC_SCORE)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;

	// set black name posstion
	wk_pos.y += SCORE_OFFSET ;
	GetDlgItem(IDC_BLACK_TIME_NAME)->GetClientRect(&rect) ;
	GetDlgItem(IDC_BLACK_TIME_NAME)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;
	
	// set black time posstion
	wk_pos.y += TIME2TEXT_OFFSET ;
	GetDlgItem(IDC_BLACK_TIME)->GetClientRect(&rect) ;
	GetDlgItem(IDC_BLACK_TIME)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;
	
	// set whilte name posstion
	wk_pos.y += TEXT2TEXT_OFFSET ;
	GetDlgItem(IDC_WHITE_TIME_NAME)->GetClientRect(&rect) ;
	GetDlgItem(IDC_WHITE_TIME_NAME)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;
	
	// set white time posstion
	wk_pos.y += TIME2TEXT_OFFSET ;
	GetDlgItem(IDC_WHITE_TIME)->GetClientRect(&rect) ;
	GetDlgItem(IDC_WHITE_TIME)->MoveWindow(wk_pos.x, wk_pos.y, rect.right, rect.bottom) ;
}

// +-+-+-+- change turn +-+-+-+-+-
void COthelloDlg::ChangeColor(int new_color)
{
	int stimer_id ;		// once stop timer ID
	int enemy_color ;	// enemy color
	char str[STR_BUFF] ;

	::memset(&str, 0, sizeof(str)) ;

	// put continue
	if(new_color == manager->color){
		if(manager->color == WHITE){
			enemy_color = BLACK ;
			stimer_id = WHITE_TIMER_ID ;
		}else{
			enemy_color = WHITE ;
			stimer_id = BLACK_TIMER_ID ;
		}

		manager->GetColorName(enemy_color, str) ;
#if ENGLISH
		strcat(str, "is not move") ;
#else
		strcat(str, "は置くところがありません。") ;
#endif
		if(time_limit != -1){
			this->KillTimer(stimer_id);
		}
		this->KillTimer(COM_TIMER_ID);
		AfxMessageBox(str) ;
		this->SetTimer(COM_TIMER_ID, manager->com_interval, NULL);
	}

	// next turn
	manager->color = new_color ;

	// set count down timer
	SetCountDownTimer(new_color) ;
}

// +-+-+-+- Swap Color +-+-+-+-+-
void COthelloDlg::SwapColor(int *color)
{
	if(*color == WHITE){
		*color = BLACK ;
	}else{
		*color = WHITE ;
	}
	return ;
}

// +-+-+- vers +-+-+-
void COthelloDlg::VersProc()
{
	int old_turn_cnt = manager->turn_cnt ;

	// if next put location is not put then not use vers proc
	if(manager->turn_cnt < manager->turning_point ){
		manager->turn_cnt ++ ;
	}

	// validate change KIHU
	if(old_turn_cnt != manager->turn_cnt){
		StrKihuTable * CurentKihu = NULL;

		CurentKihu = &manager->KihuTable[manager->turn_cnt-1];
		manager->PutStone(CurentKihu->zahyo, CurentKihu->put_stone, CurentKihu->color, VERS);

		// set turn
		manager->color = CurentKihu->color ;
		SwapColor(&manager->color);
	}
}

// +-+-+- revers +-+-+-
void COthelloDlg::ReVersProc()
{
	int old_turn_cnt = manager->turn_cnt ;

	// if before put location is init map then not use revers proc
	if(manager->turn_cnt > 0){
		manager->turn_cnt -- ;
	}

	// validate change KIHU
	if(old_turn_cnt != manager->turn_cnt){
		StrKihuTable * CurentKihu = NULL;
		int color ;

		CurentKihu = &manager->KihuTable[manager->turn_cnt];
		color = CurentKihu->color;
		SwapColor(&color);

		// set turn
		manager->PutStone(CurentKihu->zahyo, CurentKihu->put_stone, color, REVERS);
		manager->color = CurentKihu->color ;
	}
}

// +-+-+- update Rect +-+-+-
void COthelloDlg::UpdateRect()
{
	CRect rect ;	// client area
	CPoint cell ;	// work of caluclation cell size
	GetClientRect(rect) ;

	// set offset from edge of picture
	rect.top += FRAME_OFFSET ;
	rect.left += FRAME_OFFSET ;
	rect.right -= (FRAME_OFFSET + TEXT_OFFSET) ;
	rect.bottom -= FRAME_OFFSET ;

	// get table size
	cell.x = rect.right - rect.left ;
	cell.y = rect.bottom - rect.top ;

	// calculate (X、Y) matrix
	cell.x = cell.x - (cell.x % LINE_MAX) ;	// save client area(can be divided by LINE_MAX)
	cell.y = cell.y - (cell.y % LINE_MAX) ;	// save client area(can be divided by LINE_MAX)

	// save 2 minimum size(X,Y)
	if(cell.x < cell.y){
		cellsize = cell.x / LINE_MAX ;
	}else{
		cellsize = cell.y / LINE_MAX ;
	}

	// set offset from minimum
	cell_offset = cellsize * 95 / 100 ;

	// set possition remainder time
	// offset of edge picture * 2  +  max cell number  +  offset of text string view
	time_pos.x = cellsize * LINE_MAX + FRAME_OFFSET + FRAME2TEXT_OFFSET ;
	time_pos.y = cellsize ;
}

// +-+-+-+- first init proc +-+-+-+-+-
void COthelloDlg::AppInit() 
{
	int time_id ;
	static HFONT font ;	// font struct
	LOGFONT viewfont ;	// font struct
	CPoint wk_pos ;
	CRect rect ;

	::memcpy(&wk_pos, &time_pos, sizeof(CPoint)) ;
	::memset(&viewfont, 0, sizeof(viewfont)) ;

	// initialize time limit
	time_limit = DEFAULT_TIME ;
	if(time_limit == -1){
		time_id = ID_MENUITEM_TIME_NONE ;
	}else if(time_limit == 30){
		time_id = ID_MENUITEM_TIME_05 ;
	}else if(time_limit == 60){
		time_id = ID_MENUITEM_TIME_1 ;
	}else if(time_limit == 120){
		time_id = ID_MENUITEM_TIME_2 ;
	}else if(time_limit == 180){
		time_id = ID_MENUITEM_TIME_3 ;
	}else if(time_limit == 300){
		time_id = ID_MENUITEM_TIME_5 ;
	}else if(time_limit == 600){
		time_id = ID_MENUITEM_TIME_10 ;
	}else if(time_limit == 900){
		time_id = ID_MENUITEM_TIME_15 ;
	}else{
		// any way where check
		time_id = ID_MENUITEM_TIME_5 ;
	}

	// check menu bar
	SetMenuStatus(CHECK, ID_MENUITEM_PLAYMD_PP+DEFAULT_PLAY_MODE-1, MF_CHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_ComLevel1+DEFAULT_COM_LEVEL-1, MF_CHECKED ) ;
	SetMenuStatus(CHECK, time_id, MF_CHECKED ) ;
	SetMenuStatus(CHECK, ID_MENUITEM_PUT_NOTICE, DEFAULT_PUT_NOTICE) ;

	// disable button
	GetDlgItem(IDOK)->ShowWindow(FALSE) ;
	GetDlgItem(IDCANCEL)->ShowWindow(FALSE) ;

	// tool bar
#if TOOLBER_USE
	if (!othebar.CreateEx(this, TBSTYLE_FLAT, WS_CHILD | WS_VISIBLE | CBRS_TOP
		| CBRS_GRIPPER | CBRS_TOOLTIPS | CBRS_FLYBY | CBRS_SIZE_DYNAMIC) ||
		!othebar.LoadToolBar(IDR_TOOLBAR1))
	{
		TRACE0("Failed to create toolbar\n");
	}else{
		// TODO: ツール バーをドッキング可能にしない場合は以下の３行を削除
		//       してください。
//		othebar.EnableDocking(CBRS_ALIGN_ANY);
//		EnableDocking(CBRS_ALIGN_ANY);
//		othebar.DockControlBar(&toolbar);
	}
#endif

	// set turn name pissition
	GetDlgItem(IDC_TURN_TEXT)->GetClientRect(&rect) ;
	GetDlgItem(IDC_TURN_TEXT)->MoveWindow(wk_pos.x, wk_pos.y, rect.right+STR_WEIGHT, rect.bottom+STR_HEIGHT) ;

	// set score value pissition
	wk_pos.y += TIME2TEXT_OFFSET + STR_HEIGHT/4 ;
	GetDlgItem(IDC_SCORE)->GetClientRect(&rect) ;
	GetDlgItem(IDC_SCORE)->MoveWindow(wk_pos.x, wk_pos.y, rect.right+STR_WEIGHT, rect.bottom+STR_HEIGHT) ;

	// crate view font
	viewfont.lfCharSet = DEFAULT_CHARSET ;
	viewfont.lfWeight = STR_WEIGHT ;
	viewfont.lfHeight = STR_HEIGHT ;
	font = CreateFontIndirect(&viewfont) ;
	SendDlgItemMessage(IDC_TURN_TEXT, WM_SETFONT, (WPARAM)font, MAKELPARAM(TRUE, 0)) ;
	SendDlgItemMessage(IDC_SCORE, WM_SETFONT, (WPARAM)font, MAKELPARAM(TRUE, 0)) ;

	// init dialog
	GameInit() ;

	// update rect
	UpdateRect() ;

	// contraint operator
	ConstraintsOperator();
}

// +-+-+-+- set menu status +-+-+-+-+-
//	int	attrib	属性
//	int	id		ID
//	int	value	値
void COthelloDlg::SetMenuStatus(int attrib, int id, int value)
{
	// Menu pinter
	CMenu* pMenu = GetMenu();

	switch(attrib){
	case ENABLE :
		pMenu->EnableMenuItem(id, value) ;
		break ;
	case CHECK :
		pMenu->CheckMenuItem(id, value) ;
		break ;
	default :
		break ;
	}
}

// +-+-+-+- update score +-+-+-+-+-
void COthelloDlg::UpdateScore()
{
	int black_num ;
	int white_num ;
	char str[STR_BUFF] ;
	char wk[STR_BUFF] ;

	::memset(str, 0, sizeof(str)) ;

	// view which turn
	manager->GetColorName(manager->color, wk) ;
#if ENGLISH
	sprintf(str, " 【turn : %s】",wk) ;
#else
	sprintf(str, " 【%sの番】",wk) ;
#endif
	GetDlgItem(IDC_TURN_TEXT)->SetWindowText(str) ;

	// view stone number
	manager->CntStone(&black_num, &white_num) ;
#if ENGLISH
	sprintf(str, "black：%2d  white：%2d ",black_num,white_num) ;
#else
	sprintf(str, "黒：%2d　白：%2d ",black_num,white_num) ;
#endif
	GetDlgItem(IDC_SCORE)->SetWindowText(str) ;
}

void COthelloDlg::OnMenuitemKihuRead() 
{
	int rt ;
	int change_rt ;
	CString path ;

#if ENGLISH
	CFileDialog fd(TRUE, "*.rkf", NULL, OFN_HIDEREADONLY, "othello KIHU file (*.rkf)|*.rkf|All Files (*.*)|*.*||");
#else
	CFileDialog fd(TRUE, "*.rkf", NULL, OFN_HIDEREADONLY, "オセロ棋譜ファイル (*.rkf)|*.rkf|All Files (*.*)|*.*||");
#endif
	if (fd.DoModal() == IDOK)
	{
		path = fd.GetPathName();

		// init dialog
		GameInit() ;

		rt = manager->GetKihuFile(path, &manager->color) ;
		if(rt != ERROR_RETURN){
			// next turn decision + star timer

			change_rt = manager->PutCheckWhich(manager->color) ;
			if(change_rt == WHITE || change_rt == BLACK){
				ChangeColor(change_rt) ;
			}

			// star com timer
			this->SetTimer(COM_TIMER_ID, manager->com_interval, NULL);

			// update score
			UpdateScore() ;

 			CPaintDC dc(this); // device context from draw
			DrawTable(&dc) ;
		}else{
#if ENGLISH
			AfxMessageBox("read file error") ;
#else
			AfxMessageBox("ファイル読み込みエラー") ;
#endif
			GameInit() ;
		}
	}
}
