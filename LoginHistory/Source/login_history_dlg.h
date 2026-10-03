// LoginHistoryDlg.h : メインダイアログ（実行した日時をログファイルに追記する）

#pragma once

class CLoginHistoryDlg : public CDialogEx
{
public:
	explicit CLoginHistoryDlg(CWnd* pParent = nullptr);

	enum { IDD = IDD_LOGINHISTORY_DIALOG };

protected:
	virtual void DoDataExchange(CDataExchange* pDX) override;
	virtual BOOL OnInitDialog() override;

	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnBnClickedExec();
	DECLARE_MESSAGE_MAP()

private:
	HICON m_hIcon;
	CString m_strLogName;
};
