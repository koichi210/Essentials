// LoginHistory.cpp : アプリケーションクラス

#include "stdafx.h"
#include "LoginHistory.h"
#include "LoginHistoryDlg.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

BEGIN_MESSAGE_MAP(CLoginHistoryApp, CWinApp)
	ON_COMMAND(ID_HELP, &CWinApp::OnHelp)
END_MESSAGE_MAP()

CLoginHistoryApp theApp;

CLoginHistoryApp::CLoginHistoryApp()
{
	m_dwRestartManagerSupportFlags = AFX_RESTART_MANAGER_SUPPORT_RESTART;
}

BOOL CLoginHistoryApp::InitInstance()
{
	// ComCtl32.dll Version 6 を使うマニフェストの場合、これが無いとウィンドウ作成に失敗する
	INITCOMMONCONTROLSEX initCtrls = {};
	initCtrls.dwSize = sizeof(initCtrls);
	initCtrls.dwICC = ICC_WIN95_CLASSES;
	InitCommonControlsEx(&initCtrls);

	CWinApp::InitInstance();

	CLoginHistoryDlg dlg;
	m_pMainWnd = &dlg;
	dlg.DoModal();

	// ダイアログを閉じたらメッセージポンプを開始せずに終了する
	return FALSE;
}
