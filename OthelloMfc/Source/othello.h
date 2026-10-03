// othello.h : アプリケーションクラス

#ifndef OTHELLO_SOURCE_OTHELLO_H_
#define OTHELLO_SOURCE_OTHELLO_H_

#ifndef __AFXWIN_H__
	#error "PCH に対してこのファイルをインクルードする前に 'StdAfx.h' をインクルードしてください"
#endif

#include "resource.h"

class OthelloApp : public CWinApp
{
public:
	virtual BOOL InitInstance() override;

	DECLARE_MESSAGE_MAP()
};

#endif  // OTHELLO_SOURCE_OTHELLO_H_
