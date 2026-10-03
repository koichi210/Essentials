// com_player.h : COMの思考ルーチン

#pragma once

#include "board.h"

class ComPlayer
{
public:
	ComPlayer();

	// 盤面と手番から打つ座標を決める（置ける場所がなければ false）
	bool Think(const Board& board, Stone color, int level, int moveCount, CPoint& result);

private:
	// 候補手と評価値
	struct Candidate
	{
		CPoint	pos;
		int		score;
	};
	using Candidates = std::vector<Candidate>;

	Candidates GetCenterCandidates() const;		// 中央に近い順
	Candidates GetFlipCandidates(bool many) const;	// 裏返す数が多い順/少ない順
	Candidates GetOpennessCandidates() const;	// 開放度が低い順
	int CountEmptyAround(CPoint pos) const;		// 周囲8マスの空きマス数

	bool ChooseFrom(const Candidates& candidates, CPoint& result);	// 候補から1手選ぶ
	bool GivesCornerToEnemy(CPoint pos) const;	// 打った後に相手が角を取れるか

	static void SortCandidates(Candidates& candidates, bool ascending);
	static bool IsCorner(CPoint pos);
	static bool IsXSquare(CPoint pos);			// 角の斜め隣（星）
	static bool IsCSquare(CPoint pos);			// 角の縦横隣

	Board			board_;		// 思考対象の盤面
	Stone			color_;		// COMの色
	int				move_count_;	// 現在の手数
	std::mt19937	random_;
};
