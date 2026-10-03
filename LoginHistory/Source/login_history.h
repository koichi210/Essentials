// LoginHistory.h : アプリケーションクラス

#ifndef LOGINHISTORY_SOURCE_LOGIN_HISTORY_H_
#define LOGINHISTORY_SOURCE_LOGIN_HISTORY_H_

#ifndef __AFXWIN_H__
	#error "PCH に対してこのファイルをインクルードする前に 'stdafx.h' をインクルードしてください"
#endif

#include "resource.h"

class LoginHistoryApp : public CWinApp
{
public:
	LoginHistoryApp();

	virtual BOOL InitInstance() override;

	DECLARE_MESSAGE_MAP()
};

#endif  // LOGINHISTORY_SOURCE_LOGIN_HISTORY_H_
