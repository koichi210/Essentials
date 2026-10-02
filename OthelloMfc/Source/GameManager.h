// GameManager.h : 対局の進行・設定・棋譜の管理

#pragma once

#include "Board.h"

// 棋譜1手分
struct KifuRecord
{
	CPoint		pos;	// 置いた座標
	Stone		color;	// 置いた色
	FlipCounts	flips;	// 方向ごとの裏返した数（一手戻す/送るで使う）
};

class CGameManager
{
public:
	CGameManager();

	void NewGame();								// 盤面・手番・棋譜を初期化

	// 対局
	const CBoard& GetBoard() const		{ return m_board; }
	Stone GetTurn() const				{ return m_turn; }
	void SetTurn(Stone color)			{ m_turn = color; }
	int GetMoveCount() const			{ return m_moveCount; }
	bool IsComTurn() const;						// 今の手番がCOMか
	bool PlayMove(CPoint pos);					// 今の手番の色で打つ（打てなければ false）

	// 一手戻す/送る
	bool CanUndo() const				{ return m_moveCount > 0; }
	bool CanRedo() const				{ return m_moveCount < m_recordCount; }
	void Undo();
	void Redo();

	// 棋譜
	CString GetKifuText() const;				// 表示/保存用の棋譜テキスト
	bool SaveKifu(LPCTSTR path) const;
	bool LoadKifu(LPCTSTR path);				// 読み込んだ手を盤面に反映（NewGame 後に呼ぶ）

	// 状態・設定
	GameState	m_gameState;
	PlayMode	m_playMode;
	int			m_comLevel;
	bool		m_showMovable;		// 置ける場所を表示するか

private:
	CBoard	m_board;
	Stone	m_turn;					// 今の手番
	int		m_moveCount;			// 盤面に反映済みの手数
	int		m_recordCount;			// 棋譜に記録済みの手数（一手戻した分を含む）
	std::array<KifuRecord, kMaxMoves> m_kifu;
};
