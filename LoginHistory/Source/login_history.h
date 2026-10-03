// LoginHistory.h : アプリケーションクラス

#pragma once

#ifndef __AFXWIN_H__
	#error "PCH に対してこのファイルをインクルードする前に 'stdafx.h' をインクルードしてください"
#endif

#include "resource.h"

class CLoginHistoryApp : public CWinApp
{
public:
	CLoginHistoryApp();

	virtual BOOL InitInstance() override;

	DECLARE_MESSAGE_MAP()
};
