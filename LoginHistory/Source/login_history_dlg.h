// LoginHistoryDlg.h : メインダイアログ（実行した日時をログファイルに追記する）

#ifndef LOGINHISTORY_SOURCE_LOGIN_HISTORY_DLG_H_
#define LOGINHISTORY_SOURCE_LOGIN_HISTORY_DLG_H_

class LoginHistoryDlg : public CDialogEx
{
public:
	explicit LoginHistoryDlg(CWnd* parent = nullptr);

	enum { IDD = IDD_LOGINHISTORY_DIALOG };

protected:
	virtual void DoDataExchange(CDataExchange* dx) override;
	virtual BOOL OnInitDialog() override;

	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnBnClickedExec();
	DECLARE_MESSAGE_MAP()

private:
	HICON icon_;
	CString log_name_;
};

#endif  // LOGINHISTORY_SOURCE_LOGIN_HISTORY_DLG_H_
