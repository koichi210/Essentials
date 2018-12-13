// othelloDlg.h : ヘッダー ファイル
//

#if !defined(AFX_OTHELLODLG_H__EDFA78B9_2410_4CED_BA13_A4573185749A__INCLUDED_)
#define AFX_OTHELLODLG_H__EDFA78B9_2410_4CED_BA13_A4573185749A__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

/////////////////////////////////////////////////////////////////////////////
// COthelloDlg ダイアログ

class COthelloDlg : public CDialog
{
// 構築
protected:
	CManager *	manager ;	// instance manager class
	CComProc * com ;		// instance COM class
	CPoint	ldown ;			// save left click matrix
	CPoint	time_pos ;		// view position of time out
#if TOOLBER_USE
	CToolBar	othebar ;	// toolbar
#endif
	int		cellsize ;		// save cell size
	int		cell_offset ;	// between end cell and stone to space
	int		black_time ;	// black time
	int		white_time ;	// white time
	int		time_limit ;	// limit time

public:
	COthelloDlg(CWnd* pParent = NULL);	// baseic construct

	void DrawTable(CDC * pDC) ;		// draw table
	void DrawStone(CDC * pDC, CPoint zahyo,int clr) ;	// draw stone
	void DrawNotice(CDC * pDC, int turn) ;		// draw small circle
	void DrawNumber(CDC * pDC, CPoint zahyo) ;	// draw cell number

	void SetMenuPlayMode(int ID) ;	// set play mode
	void SetMenuComLevel(int ID) ;	// set COM level
	void SetMenuTime(int ID, int time) ;	// set time
	void SetMenuStatus(int attrib, int id, int value) ;	// set Menu value
//	void SetTitleBar() ;
	void Constraints(BOOL flg) ;	// constraint
	void ConstraintsOperator() ;	// constraint operator

	void UpdateRect() ;				// update Rect
	void UpdatePos() ;				// update position
	void UpdateScore() ;			// update score

	void GameInit() ;				// init set value
	void AppInit() ;				// initial proc
	void StartProc() ;				// start proc
	void EndProc(int state) ;		// end proc
	void TimeOutProc() ;			// time out proc

	int  OthelloProc(CPoint zahyo) ;	// main othello proc
	int  ComRoutin() ;					// com turn
	void SetCountDownTimer(int attrib) ;	// timer of count down
	void SetTimerString() ;				// view remainder time
	void ChangeColor(int change_turn) ;	// change turn
	void SwapColor(int * color) ;			// swap color
	void VersProc() ;					// vers
	void ReVersProc() ;					// revers


// dialog data
	//{{AFX_DATA(COthelloDlg)
	enum { IDD = IDD_OTHELLO_DIALOG };
		// メモ: この位置に ClassWizard によってデータ メンバが追加されます。
	//}}AFX_DATA

	// ClassWizard は仮想関数のオーバーライドを生成します。
	//{{AFX_VIRTUAL(COthelloDlg)
	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV のサポート
	//}}AFX_VIRTUAL

// インプリメンテーション
protected:
	HICON m_hIcon;

	// 生成されたメッセージ マップ関数
	//{{AFX_MSG(COthelloDlg)
	virtual BOOL OnInitDialog();
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnPaint();
	afx_msg void OnDestroy();
	afx_msg void OnGetMinMaxInfo(MINMAXINFO FAR* lpMMI);
	afx_msg void OnSize(UINT nType, int cx, int cy);
	virtual void OnOK();
	virtual void OnCancel();
	afx_msg void OnTimer(UINT nIDEvent);
	afx_msg void OnLButtonDown(UINT nFlags, CPoint point);
	afx_msg void OnLButtonUp(UINT nFlags, CPoint point);
	afx_msg void OnRButtonDown(UINT nFlags, CPoint point);
	afx_msg void OnMenuitemStart();
	afx_msg void OnMenuitemStop();
	afx_msg void OnMenuitemExit();
	afx_msg void OnMenuitemReset();
	afx_msg void OnMenuitemPlaymdPp();
	afx_msg void OnMenuitemPlaymdPc();
	afx_msg void OnMenuitemPlaymdCp();
	afx_msg void OnMenuitemPlaymdCc();
	afx_msg void OnMenuitemComLevel1();
	afx_msg void OnMenuitemComLevel2();
	afx_msg void OnMenuitemComLevel3();
	afx_msg void OnMenuitemTime0();
	afx_msg void OnMenuitemTime1();
	afx_msg void OnMenuitemTime2();
	afx_msg void OnMenuitemTime3();
	afx_msg void OnMenuitemTime5();
	afx_msg void OnMenuitemTime10();
	afx_msg void OnMenuitemTime15();
	afx_msg void OnMenuitemKihuShow();
	afx_msg void OnMenuitemKihuSave();
	afx_msg void OnMenuitemVers();
	afx_msg void OnMenuitemRevers();
	afx_msg void OnMenuitemVersion();
	afx_msg void OnMenuitemHowtoplay();
	afx_msg void OnMenuitemKihuRead();
	afx_msg void OnMenuitemTimeNone();
	afx_msg void OnMenuitemPutNotice();
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()

};

//{{AFX_INSERT_LOCATION}}
// Microsoft Visual C++ は前行の直前に追加の宣言を挿入します。

#endif // !defined(AFX_OTHELLODLG_H__EDFA78B9_2410_4CED_BA13_A4573185749A__INCLUDED_)
