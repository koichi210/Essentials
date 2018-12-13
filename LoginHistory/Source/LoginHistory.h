
// LoginHistory.h : PROJECT_NAME アプリケーションのメイン ヘッダー ファイルです。
//

#pragma once

#ifndef __AFXWIN_H__
	#error "PCH に対してこのファイルをインクルードする前に 'stdafx.h' をインクルードしてください"
#endif

#include "resource.h"		// メイン シンボル


// CLoginHistoryApp:
// このクラスの実装については、LoginHistory.cpp を参照してください。
//

class CLoginHistoryApp : public CWinApp
{
public:
	CLoginHistoryApp();

// オーバーライド
public:
	virtual BOOL InitInstance();

// 実装

	DECLARE_MESSAGE_MAP()
};

extern CLoginHistoryApp theApp;