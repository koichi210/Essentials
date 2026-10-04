// com_player.cc : COMの思考ルーチン

#include "StdAfx.h"
#include "com_player.h"

namespace
{
	// 局面の段階（手数で判定）
	enum class Phase
	{
		kOpening,	// 序盤
		kMiddle,	// 中盤
		kEnding,	// 終盤
	};

	Phase GetPhase(int move_count)
	{
		const int cell_count = kBoardSize * kBoardSize;
		if (move_count < cell_count / 3)
		{
			return Phase::kOpening;
		}
		if (move_count < cell_count / 3 * 2)
		{
			return Phase::kMiddle;
		}
		return Phase::kEnding;
	}

	// 中央から外側へ向かう探索順
	const int kCenterFirstOrder[kBoardSize] = { 4, 5, 3, 6, 2, 7, 1, 8 };

	// 序盤はこの手数までランダムに打つ
	const int kRandomMoveCount = 2;
}

ComPlayer::ComPlayer()
	: color_(Stone::kBlack)
	, move_count_(0)
	, random_(std::random_device()())
{
}

// レベル1: 中央寄り / レベル2: 序盤は中央寄り・中盤は少なく返す・終盤は多く返す / レベル3: 開放度
bool ComPlayer::Think(const Board& board, Stone color, int level, int move_count, CPoint& result)
{
	result = CPoint(0, 0);
	if (move_count < 0 || kMaxMoves < move_count)
	{
		return false;
	}

	board_ = board;
	color_ = color;
	move_count_ = move_count;

	Candidates candidates;
	switch (level)
	{
	case 1:
		candidates = GetCenterCandidates();
		break;

	case 2:
		switch (GetPhase(move_count))
		{
		case Phase::kOpening:	candidates = GetCenterCandidates();		break;
		case Phase::kMiddle:	candidates = GetFlipCandidates(false);	break;
		default:				candidates = GetFlipCandidates(true);	break;
		}
		break;

	case 3:
		candidates = GetOpennessCandidates();
		break;

	default:
		{
			// 想定外のレベルは左上から探して最初に置ける場所
			const std::vector<CPoint> movable = board_.GetMovablePositions(color_);
			if (movable.empty())
			{
				return false;
			}
			result = movable.front();
			return true;
		}
	}

	return ChooseFrom(candidates, result) && Board::IsInside(result);
}

ComPlayer::Candidates ComPlayer::GetCenterCandidates() const
{
	const int center = kBoardSize / 2;
	Candidates candidates;

	for (int y : kCenterFirstOrder)
	{
		for (int x : kCenterFirstOrder)
		{
			const CPoint pos(x, y);
			if (board_.CanPut(pos, color_))
			{
				candidates.push_back({ pos, abs(center - x) + abs(center - y) });
			}
		}
	}

	SortCandidates(candidates, true);
	return candidates;
}

ComPlayer::Candidates ComPlayer::GetFlipCandidates(bool many) const
{
	Candidates candidates;

	for (const CPoint& pos : board_.GetMovablePositions(color_))
	{
		FlipCounts flips;
		board_.GetFlips(pos, color_, flips);
		candidates.push_back({ pos, std::accumulate(flips.begin(), flips.end(), 0) });
	}

	SortCandidates(candidates, !many);
	return candidates;
}

// 開放度 = 置いたマスと裏返る石それぞれの周囲にある空きマスの合計
// 小さいほど相手に打たれる場所を増やさない良い手とみなす
ComPlayer::Candidates ComPlayer::GetOpennessCandidates() const
{
	Candidates candidates;

	for (const CPoint& pos : board_.GetMovablePositions(color_))
	{
		FlipCounts flips;
		board_.GetFlips(pos, color_, flips);

		// 盤面を写して全マスを比べなくても、裏返る石は方向ごとの数から辿れる
		int openness = CountEmptyAround(pos);
		for (int dir = 0; dir < kDirectionCount; dir++)
		{
			CPoint cell = pos;
			for (int i = 0; i < flips[dir]; i++)
			{
				cell += Board::kDirections[dir];
				openness += CountEmptyAround(cell);
			}
		}
		candidates.push_back({ pos, openness });
	}

	SortCandidates(candidates, true);
	return candidates;
}

// 打つ前の盤面で数える
int ComPlayer::CountEmptyAround(CPoint pos) const
{
	int count = 0;

	for (int dy = -1; dy <= 1; dy++)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			const CPoint around(pos.x + dx, pos.y + dy);
			if ((dx != 0 || dy != 0) && Board::IsInside(around) && board_.GetAt(around) == Stone::kNone)
			{
				count++;
			}
		}
	}

	return count;
}

// 序盤はランダム。それ以降は候補を評価順に見て、
// 角 > 無難な手 > 星 > 角の隣 > 相手に角を与える手 の優先度で、各分類の最初の候補を選ぶ
bool ComPlayer::ChooseFrom(const Candidates& candidates, CPoint& result)
{
	if (candidates.empty())
	{
		return false;
	}

	if (move_count_ <= kRandomMoveCount)
	{
		std::uniform_int_distribution<size_t> dist(0, candidates.size() - 1);
		result = candidates[dist(random_)].pos;
		return true;
	}

	enum { kCorner, kSafe, kXSquare, kCSquare, kGivesCorner, kCategoryCount };
	const CPoint* best[kCategoryCount] = {};

	for (const Candidate& candidate : candidates)
	{
		const CPoint& pos = candidate.pos;
		int category;
		if (IsCorner(pos))
		{
			category = kCorner;
		}
		else if (GivesCornerToEnemy(pos))
		{
			category = kGivesCorner;
		}
		else if (IsXSquare(pos))
		{
			category = kXSquare;
		}
		else if (IsCSquare(pos))
		{
			category = kCSquare;
		}
		else
		{
			category = kSafe;
		}

		if (best[category] == nullptr)
		{
			best[category] = &pos;
		}
	}

	for (const CPoint* pos : best)
	{
		if (pos != nullptr)
		{
			result = *pos;
			return true;
		}
	}
	return false;
}

bool ComPlayer::GivesCornerToEnemy(CPoint pos) const
{
	FlipCounts flips;
	if (!board_.GetFlips(pos, color_, flips))
	{
		return false;
	}

	Board after = board_;
	after.Put(pos, flips, color_);

	const Stone enemy = Opponent(color_);
	const int edges[] = { 1, kBoardSize };
	for (int y : edges)
	{
		for (int x : edges)
		{
			if (after.CanPut(CPoint(x, y), enemy))
			{
				return true;
			}
		}
	}
	return false;
}

// 同点のときの並びを従来どおりに保つため、単純な交換ソートで並べる
void ComPlayer::SortCandidates(Candidates& candidates, bool ascending)
{
	for (size_t i = 0; i < candidates.size(); i++)
	{
		for (size_t j = i + 1; j < candidates.size(); j++)
		{
			const bool need_swap = ascending
				? (candidates[i].score > candidates[j].score)
				: (candidates[i].score < candidates[j].score);
			if (need_swap)
			{
				std::swap(candidates[i], candidates[j]);
			}
		}
	}
}

bool ComPlayer::IsCorner(CPoint pos)
{
	const bool edge_x = (pos.x == 1 || pos.x == kBoardSize);
	const bool edge_y = (pos.y == 1 || pos.y == kBoardSize);
	return edge_x && edge_y;
}

bool ComPlayer::IsXSquare(CPoint pos)
{
	const bool next_x = (pos.x == 2 || pos.x == kBoardSize - 1);
	const bool next_y = (pos.y == 2 || pos.y == kBoardSize - 1);
	return next_x && next_y;
}

bool ComPlayer::IsCSquare(CPoint pos)
{
	const bool edge_x = (pos.x == 1 || pos.x == kBoardSize);
	const bool edge_y = (pos.y == 1 || pos.y == kBoardSize);
	const bool next_x = (pos.x == 2 || pos.x == kBoardSize - 1);
	const bool next_y = (pos.y == 2 || pos.y == kBoardSize - 1);
	return (edge_x && next_y) || (next_x && edge_y);
}
