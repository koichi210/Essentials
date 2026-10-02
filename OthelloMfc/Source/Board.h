// Board.h : 盤面の保持と着手判定

#pragma once

#include "OthelloDefs.h"

// 盤面クラス
// 座標は棋譜表記と合わせて 1〜kBoardSize の1始まりで扱う
class CBoard
{
public:
	CBoard();

	void Reset();									// 初期配置に戻す

	static bool IsInside(CPoint pos);				// 盤内の座標か
	Stone GetAt(CPoint pos) const;					// マスの状態を取得

	bool CanPut(CPoint pos, Stone color) const;							// 置けるか（裏返る石があるか）
	bool GetFlips(CPoint pos, Stone color, FlipCounts& flips) const;	// 方向ごとの裏返る数を取得
	std::vector<CPoint> GetMovablePositions(Stone color) const;		// 置ける座標の一覧（y→xの順）
	bool HasMovablePosition(Stone color) const;							// 置ける場所が1つでもあるか
	Stone GetNextTurn(Stone mover) const;								// 次の手番（終局なら Stone::None）

	void Put(CPoint pos, const FlipCounts& flips, Stone color);			// 石を置いて裏返す
	void Undo(CPoint pos, const FlipCounts& flips, Stone mover);		// Put を取り消す

	void CountStones(int& black, int& white) const;	// 石数を数える

private:
	static const CPoint kDirections[kDirectionCount];	// 方向ごとの移動量

	Stone& At(CPoint pos);
	void Flip(CPoint pos, const FlipCounts& flips, Stone color);	// 裏返る石を color にする

	Stone m_cells[kBoardSize][kBoardSize];
};
