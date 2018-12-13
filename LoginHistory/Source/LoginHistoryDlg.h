
// LoginHistoryDlg.h : ヘッダー ファイル
//

#pragma once

#define INI_FILE_NAME	"set.ini"
#define LOG_FILE_NAME	"C:\\Alive.log"
#define FILE_NAME		"FileName"
#define CYCLE			"Cycle"
#define WARM			" !"
#define COMENT			";"

#define UPDATE_CYC		(60)	// min
#define STR_BUFF		(30)
#define SBCS			(0)
#define DBCS			(1)
#define	NORMAL			(0)
#define ERROR			(-1)
#define TRUE			(1)
#define	FALSE			(0)
#define	MAX_MON			(12)
#define	MAX_DAY			(31)
#define	MAX_HOUR		(24)
#define	MAX_MIN			(60)
#define	MAX_SEC			(60)
#define	DIFFER			(10)
#define MOUNTH_OFFSET	(1)
#define YEAR_OFFSET		(1900)


// CLoginHistoryDlg ダイアログ
class CLoginHistoryDlg : public CDialogEx
{
// コンストラクション
public:
	CLoginHistoryDlg(CWnd* pParent = NULL);	// 標準コンストラクター

// ダイアログ データ
	enum { IDD = IDD_LOGINHISTORY_DIALOG };

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV サポート

	CString m_Logname;

// 実装
protected:
	HICON m_hIcon;

	// 生成された、メッセージ割り当て関数
	virtual BOOL OnInitDialog();
	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnBnClickedExec();
	DECLARE_MESSAGE_MAP()
};

void LoadIniFile(char fname[STR_BUFF], int * cyc) ;
void CreateFile(char fname[STR_BUFF], tm * time) ;
void GetLastUpDate(FILE * fp, tm * time) ;
int  GetTimeString(char time_str[STR_BUFF], tm * time) ;
int  CheckDate(tm * t_time) ;
int  CheckUpDateTime(tm * o_time, tm * n_time, int cyc) ;
