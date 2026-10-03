// othello.cc : アプリケーションクラス

#include "StdAfx.h"
#include "othello.h"
#include "othello_dlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

BEGIN_MESSAGE_MAP(COthelloApp, CWinApp)
	ON_COMMAND(ID_HELP, &CWinApp::OnHelp)
END_MESSAGE_MAP()

COthelloApp theApp;

BOOL COthelloApp::InitInstance()
{
	CWinApp::InitInstance();

	COthelloDlg dlg;
	m_pMainWnd = &dlg;
	dlg.DoModal();

	// ダイアログを閉じたらメッセージポンプを開始せずに終了する
	return FALSE;
}
