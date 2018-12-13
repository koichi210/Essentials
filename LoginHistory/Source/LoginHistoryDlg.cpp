
// LoginHistoryDlg.cpp : 実装ファイル
//

#include "stdafx.h"
#include "LoginHistory.h"
#include "LoginHistoryDlg.h"
#include "afxdialogex.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif


// CLoginHistoryDlg ダイアログ
CLoginHistoryDlg::CLoginHistoryDlg(CWnd* pParent /*=NULL*/)
	: CDialogEx(CLoginHistoryDlg::IDD, pParent)
	, m_Logname(_T(""))
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CLoginHistoryDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
	DDX_Text(pDX, IDET_LOGNAME, m_Logname);
}

BEGIN_MESSAGE_MAP(CLoginHistoryDlg, CDialogEx)
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	ON_BN_CLICKED(IDBT_EXEC, &CLoginHistoryDlg::OnBnClickedExec)
END_MESSAGE_MAP()


// CLoginHistoryDlg メッセージ ハンドラー

BOOL CLoginHistoryDlg::OnInitDialog()
{
	CDialogEx::OnInitDialog();

	// このダイアログのアイコンを設定します。アプリケーションのメイン ウィンドウがダイアログでない場合、
	//  Framework は、この設定を自動的に行います。
	SetIcon(m_hIcon, TRUE);			// 大きいアイコンの設定
	SetIcon(m_hIcon, FALSE);		// 小さいアイコンの設定

	// TODO: 初期化をここに追加します。
	m_Logname = LOG_FILE_NAME;
	UpdateData(FALSE);

	return TRUE;  // フォーカスをコントロールに設定した場合を除き、TRUE を返します。
}

// ダイアログに最小化ボタンを追加する場合、アイコンを描画するための
//  下のコードが必要です。ドキュメント/ビュー モデルを使う MFC アプリケーションの場合、
//  これは、Framework によって自動的に設定されます。

void CLoginHistoryDlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this); // 描画のデバイス コンテキスト

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		// クライアントの四角形領域内の中央
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// アイコンの描画
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialogEx::OnPaint();
	}
}

// ユーザーが最小化したウィンドウをドラッグしているときに表示するカーソルを取得するために、
//  システムがこの関数を呼び出します。
HCURSOR CLoginHistoryDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}

void LoadIniFile(char * fname, int * cyc)
{
	FILE *fp ;
	char buff[STR_BUFF] ;

	memset(buff, 0 ,sizeof(buff)) ;

	fp = fopen(INI_FILE_NAME, "r") ;
	if(fp != NULL){
		while(fgets(buff, sizeof(buff), fp)){
			if(strncmp(buff, COMENT, strlen(COMENT)) == 0){
				continue;
			}else if(strncmp(buff, FILE_NAME, strlen(FILE_NAME)) == 0){
				sscanf(buff, "FileName=%s", fname) ;
			}else if(strncmp(buff, CYCLE, strlen(CYCLE)) == 0){
				sscanf(buff, "Cycle=%d", cyc) ;
			}
		}
		fclose(fp) ;
	}
}

void CreateFile(char * fname, tm * time)
{
	FILE * fp ;
	char time_str[STR_BUFF];

	memset(time_str, 0, sizeof(time_str));

	GetTimeString(time_str, time);
	fp = fopen(fname, "w") ;
	if(fp != NULL){
		fprintf(fp,"%s\n",time_str);
		fclose(fp) ;
	}
}

int GetTimeString(char * time_str, tm *time)
{
	sprintf(time_str, "%04d.%02d.%02d_%02d:%02d:%02d",
		time->tm_year + YEAR_OFFSET,
		time->tm_mon + MOUNTH_OFFSET,
		time->tm_mday,
		time->tm_hour,
		time->tm_min,
		time->tm_sec) ;

	return CheckDate(time) ;
}

void GetLastUpDate(FILE * fp, tm * time)
{
	tm tt;
	int rt;
	char buff[STR_BUFF] ;

	memset(buff, 0, sizeof(buff));
	memset(&tt, 0, sizeof(tt));

	fseek(fp,-(STR_BUFF),SEEK_END);
	while(fgets(buff, sizeof(buff), fp)){
		sscanf(buff, "%d.%02d.%02d_%02d:%02d:%02d",
			&tt.tm_year,
			&tt.tm_mon,
			&tt.tm_mday,
			&tt.tm_hour,
			&tt.tm_min,
			&tt.tm_sec) ;
		tt.tm_mon -= MOUNTH_OFFSET ;
		tt.tm_year -= YEAR_OFFSET ;

		rt = CheckDate(&tt) ;
		if(rt == NORMAL){
			*time = tt ;
		}
	}
}

int	CheckDate(tm *t_time)
{
	tm tmp_time ;
	int rt = ERROR ;

	memcpy(&tmp_time, t_time, sizeof(tmp_time));
	tmp_time.tm_year =+ YEAR_OFFSET;
	tmp_time.tm_mon =+ MOUNTH_OFFSET;

	if(0 < tmp_time.tm_year &&
		0 < tmp_time.tm_mon && tmp_time.tm_mon <= MAX_MON &&
		0 < tmp_time.tm_mday && tmp_time.tm_mday <= MAX_DAY &&
		0 <= tmp_time.tm_hour && tmp_time.tm_hour < MAX_HOUR &&
		0 <= tmp_time.tm_min && tmp_time.tm_min < MAX_MIN &&
		0 <= tmp_time.tm_sec && tmp_time.tm_sec < MAX_SEC )
		rt = NORMAL ;

	return rt;
}

int CheckUpDateTime(tm * o_time, tm * n_time, int cyc)
{
	tm wk_time;
	int rt = TRUE ;
	int new_time ;
	int old_time ;
	int diff_time;

	memcpy(&wk_time,n_time,sizeof(tm));
	// check time
	new_time = mktime(n_time) ;
	old_time = mktime(o_time) ;

	diff_time = new_time - (old_time + cyc * MAX_SEC);
	abs(diff_time);
	if(diff_time > DIFFER){
		// time lag is DIFFER(10) sec over
		rt = FALSE ;
	}

	return rt ;
}



void CLoginHistoryDlg::OnBnClickedExec()
{
	FILE * fp ;						// file pointer
    tm * o_time = NULL ;			// old time(sec)
    tm * n_time = NULL ;			// new time(sec)
	time_t now = 0 ;				// now time
	int cycle  = UPDATE_CYC ;		// update cycle

	UpdateData();

	// load ini file
	LoadIniFile(INI_FILE_NAME, &cycle) ;

	// Get time as long integer.
	time( &now ) ;
    n_time = localtime( &now ) ;

	// opend update file
	fp = fopen(m_Logname, "r+") ;
	if(fp == NULL){
		// create new file
		CreateFile((LPTSTR)(LPCTSTR)m_Logname, n_time) ;
	}else{
		// write file
		char time_str[STR_BUFF];
		memset(time_str, 0, sizeof(time_str));

		GetTimeString(time_str, n_time);
		o_time = (tm *)malloc(sizeof(tm));
		GetLastUpDate(fp, o_time) ;
		if(CheckUpDateTime(o_time, n_time, cycle) == FALSE){
			strcat(time_str,WARM);
		}
		free(o_time);

		fseek(fp,0,SEEK_END);
		fprintf(fp,"%s\n",time_str);
		fclose(fp);
	}
}
