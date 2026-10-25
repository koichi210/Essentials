// othello.cc : アプリケーションクラス

#include "StdAfx.h"
#include "othello.h"
#include "othello_dlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

BEGIN_MESSAGE_MAP(OthelloApp, CWinApp)
	ON_COMMAND(ID_HELP, &CWinApp::OnHelp)
END_MESSAGE_MAP()

OthelloApp the_app;

BOOL OthelloApp::InitInstance()
{
	CWinApp::InitInstance();

	OthelloDlg dlg;
	m_pMainWnd = &dlg;
	dlg.DoModal();

	// ダイアログを閉じたらメッセージポンプを開始せずに終了する
	return FALSE;
}
