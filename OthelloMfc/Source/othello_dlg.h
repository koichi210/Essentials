// othello_dlg.h : メインダイアログ（盤面の描画と操作）

#ifndef OTHELLO_SOURCE_OTHELLO_DLG_H_
#define OTHELLO_SOURCE_OTHELLO_DLG_H_

#include "game_manager.h"
#include "com_player.h"

class OthelloDlg : public CDialog
{
public:
	explicit OthelloDlg(CWnd* parent = nullptr);

	enum { IDD = IDD_OTHELLO_DIALOG };

protected:
	virtual BOOL OnInitDialog() override;
	virtual void OnOK() override;

	// Windows メッセージ
	afx_msg void OnSysCommand(UINT id, LPARAM param);
	afx_msg HCURSOR OnQueryDragIcon();
	afx_msg void OnPaint();
	afx_msg void OnGetMinMaxInfo(MINMAXINFO* min_max_info);
	afx_msg void OnSize(UINT type, int cx, int cy);
	afx_msg void OnTimer(UINT_PTR event_id);
	afx_msg void OnLButtonDown(UINT flags, CPoint point);
	afx_msg void OnLButtonUp(UINT flags, CPoint point);

	// メニュー
	afx_msg void OnGameStart();
	afx_msg void OnGameStop();
	afx_msg void OnGameReset();
	afx_msg void OnGameExit();
	afx_msg void OnToggleShowMovable();
	afx_msg void OnPlayMode(UINT id);
	afx_msg void OnComLevel(UINT id);
	afx_msg void OnTimeLimit(UINT id);
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
	void SetTimeLimit(int seconds, UINT menu_id);

	// 画面の更新
	void UpdateLayout();
	void UpdateLabelPositions();
	void UpdateTimeLabels();
	void UpdateScore();
	void UpdateUndoRedoMenus();
	void EnableTimeLimitMenus(bool enable);
	void EnableMenu(UINT id, bool enable);
	void CheckMenu(UINT id, bool check);
	void CheckMenuInRange(UINT first_id, UINT last_id, UINT check_id);

	// 描画
	void DrawBoard(CDC& dc);
	void DrawStone(CDC& dc, CPoint cell, Stone color);
	void DrawMovableMarks(CDC& dc);
	CPoint HitTestCell(CPoint point) const;		// クリック位置→マス（盤外は (0,0)）

	GameManager	game_;
	ComPlayer		com_;
	HICON			icon_;
	CFont			label_font_;
	CPoint			pressed_cell_;		// 左ボタンを押したマス
	CPoint			info_pos_;			// 手番・スコア・残り時間の表示位置
	int				cell_size_;			// 1マスの大きさ(px)
	int				time_limit_sec_;		// 持ち時間(秒)。kNoTimeLimit なら制限なし
	int				black_remain_ms_;	// 黒の残り時間(ms)
	int				white_remain_ms_;	// 白の残り時間(ms)
};

#endif  // OTHELLO_SOURCE_OTHELLO_DLG_H_
