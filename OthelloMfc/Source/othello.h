// othello.h : アプリケーションクラス

#pragma once

#ifndef __AFXWIN_H__
	#error "PCH に対してこのファイルをインクルードする前に 'StdAfx.h' をインクルードしてください"
#endif

#include "resource.h"

class COthelloApp : public CWinApp
{
public:
	virtual BOOL InitInstance() override;

	DECLARE_MESSAGE_MAP()
};
