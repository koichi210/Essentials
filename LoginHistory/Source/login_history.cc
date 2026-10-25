// login_history.cc : アプリケーションクラス

#include "stdafx.h"
#include "login_history.h"
#include "login_history_dlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

BEGIN_MESSAGE_MAP(LoginHistoryApp, CWinApp)
	ON_COMMAND(ID_HELP, &CWinApp::OnHelp)
END_MESSAGE_MAP()

LoginHistoryApp the_app;

LoginHistoryApp::LoginHistoryApp()
{
	m_dwRestartManagerSupportFlags = AFX_RESTART_MANAGER_SUPPORT_RESTART;
}

BOOL LoginHistoryApp::InitInstance()
{
	// ComCtl32.dll Version 6 を使うマニフェストの場合、これが無いとウィンドウ作成に失敗する
	INITCOMMONCONTROLSEX init_ctrls = {};
	init_ctrls.dwSize = sizeof(init_ctrls);
	init_ctrls.dwICC = ICC_WIN95_CLASSES;
	InitCommonControlsEx(&init_ctrls);

	CWinApp::InitInstance();

	LoginHistoryDlg dlg;
	m_pMainWnd = &dlg;
	dlg.DoModal();

	// ダイアログを閉じたらメッセージポンプを開始せずに終了する
	return FALSE;
}
