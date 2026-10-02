// OthelloDlg.h : メインダイアログ（盤面の描画と操作）

#pragma once

#include "GameManager.h"
#include "ComPlayer.h"

class COthelloDlg : public CDialog
{
public:
	explicit COthelloDlg(CWnd* pParent = nullptr);

	enum { IDD = IDD_OTHELLO_DIALOG };

protected:
	virtual BOOL OnInitDialog() override;
	virtual void OnOK() override;

	// Windows メッセージ
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnPaint();
	afx_msg void OnGetMinMaxInfo(MINMAXINFO* lpMMI);
	afx_msg void OnSize(UINT nType, int cx, int cy);
	afx_msg void OnTimer(UINT_PTR nIDEvent);
	afx_msg void OnLButtonDown(UINT nFlags, CPoint point);
	afx_msg void OnLButtonUp(UINT nFlags, CPoint point);

	// メニュー
	afx_msg void OnGameStart();
	afx_msg void OnGameStop();
	afx_msg void OnGameReset();
	afx_msg void OnGameExit();
	afx_msg void OnToggleShowMovable();
	afx_msg void OnPlayMode(UINT nID);
	afx_msg void OnComLevel(UINT nID);
	afx_msg void OnTimeLimit(UINT nID);
	afx_msg void OnKifuShow();
	afx_msg void OnKifuSave();
	afx_msg void OnKifuRead();
	afx_msg void OnRedo();
	afx_msg void OnUndo();
	afx_msg void OnVersion();
	afx_msg void OnHowToPlay();
	DECLARE_MESSAGE_MAP()

private:
	// 対局の進行
	void NewGame();
	void StartGame();
	void EndGame(bool timeout);
	bool PlayMove(CPoint pos);
	void PlayComTurn();
	void ChangeTurn(Stone next);
	void StartCountDown(Stone color);
	void KillAllTimers();
	void SetTimeLimit(int seconds, UINT menuId);

	// 画面の更新
	void UpdateLayout();
	void UpdateLabelPositions();
	void UpdateTimeLabels();
	void UpdateScore();
	void UpdateUndoRedoMenus();
	void EnableTimeLimitMenus(bool enable);
	void EnableMenu(UINT id, bool enable);
	void CheckMenu(UINT id, bool check);
	void CheckMenuInRange(UINT firstId, UINT lastId, UINT checkId);

	// 描画
	void DrawBoard(CDC& dc);
	void DrawStone(CDC& dc, CPoint cell, Stone color);
	void DrawMovableMarks(CDC& dc);
	CPoint HitTestCell(CPoint point) const;		// クリック位置→マス（盤外は (0,0)）

	CGameManager	m_game;
	CComPlayer		m_com;
	HICON			m_hIcon;
	CFont			m_labelFont;
	CPoint			m_pressedCell;		// 左ボタンを押したマス
	CPoint			m_infoPos;			// 手番・スコア・残り時間の表示位置
	int				m_cellSize;			// 1マスの大きさ(px)
	int				m_timeLimitSec;		// 持ち時間(秒)。kNoTimeLimit なら制限なし
	int				m_blackRemainMs;	// 黒の残り時間(ms)
	int				m_whiteRemainMs;	// 白の残り時間(ms)
};
