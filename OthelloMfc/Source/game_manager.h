// game_manager.h : 対局の進行・設定・棋譜の管理

#ifndef OTHELLO_SOURCE_GAME_MANAGER_H_
#define OTHELLO_SOURCE_GAME_MANAGER_H_

#include "board.h"

// 棋譜1手分
struct KifuRecord
{
	CPoint		pos;	// 置いた座標
	Stone		color;	// 置いた色
	FlipCounts	flips;	// 方向ごとの裏返した数（一手戻す/送るで使う）
};

class GameManager
{
public:
	GameManager();

	void NewGame();								// 盤面・手番・棋譜を初期化

	// 対局
	const Board& GetBoard() const		{ return board_; }
	Stone GetTurn() const				{ return turn_; }
	void SetTurn(Stone color)			{ turn_ = color; }
	int GetMoveCount() const			{ return move_count_; }
	bool IsComTurn() const;						// 今の手番がCOMか
	bool PlayMove(CPoint pos);					// 今の手番の色で打つ（打てなければ false）

	// 一手戻す/送る
	bool CanUndo() const				{ return move_count_ > 0; }
	bool CanRedo() const				{ return move_count_ < record_count_; }
	void Undo();
	void Redo();

	// 棋譜
	CString GetKifuText() const;				// 表示/保存用の棋譜テキスト
	bool SaveKifu(LPCTSTR path) const;
	bool LoadKifu(LPCTSTR path);				// 読み込んだ手を盤面に反映（NewGame 後に呼ぶ）

	// 状態・設定
	GameState GetGameState() const		{ return game_state_; }
	void SetGameState(GameState state)	{ game_state_ = state; }
	PlayMode GetPlayMode() const		{ return play_mode_; }
	void SetPlayMode(PlayMode mode)		{ play_mode_ = mode; }
	int GetComLevel() const				{ return com_level_; }
	void SetComLevel(int level)			{ com_level_ = level; }
	bool GetShowMovable() const			{ return show_movable_; }
	void SetShowMovable(bool show)		{ show_movable_ = show; }

private:
	GameState	game_state_;
	PlayMode	play_mode_;
	int			com_level_;
	bool		show_movable_;		// 置ける場所を表示するか
	Board	board_;
	Stone	turn_;					// 今の手番
	int		move_count_;			// 盤面に反映済みの手数
	int		record_count_;			// 棋譜に記録済みの手数（一手戻した分を含む）
	std::array<KifuRecord, kMaxMoves> kifu_;
};

#endif  // OTHELLO_SOURCE_GAME_MANAGER_H_
