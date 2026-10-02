// ComPlayer.cpp : COMの思考ルーチン

#include "StdAfx.h"
#include "ComPlayer.h"

namespace
{
	// 局面の段階（手数で判定）
	enum class Phase
	{
		Opening,	// 序盤
		Middle,		// 中盤
		Ending,		// 終盤
	};

	Phase GetPhase(int moveCount)
	{
		const int cellCount = kBoardSize * kBoardSize;
		if (moveCount < cellCount / 3)
		{
			return Phase::Opening;
		}
		if (moveCount < cellCount / 3 * 2)
		{
			return Phase::Middle;
		}
		return Phase::Ending;
	}

	// 中央から外側へ向かう探索順
	const int kCenterFirstOrder[kBoardSize] = { 4, 5, 3, 6, 2, 7, 1, 8 };

	// 序盤はこの手数までランダムに打つ
	const int kRandomMoveCount = 2;
}

CComPlayer::CComPlayer()
	: m_color(Stone::Black)
	, m_moveCount(0)
	, m_random(std::random_device()())
{
}

// レベル1: 中央寄り / レベル2: 序盤は中央寄り・中盤は少なく返す・終盤は多く返す / レベル3: 開放度
bool CComPlayer::Think(const CBoard& board, Stone color, int level, int moveCount, CPoint& result)
{
	result = CPoint(0, 0);
	if (moveCount < 0 || kMaxMoves < moveCount)
	{
		return false;
	}

	m_board = board;
	m_color = color;
	m_moveCount = moveCount;

	Candidates candidates;
	switch (level)
	{
	case 1:
		candidates = GetCenterCandidates();
		break;

	case 2:
		switch (GetPhase(moveCount))
		{
		case Phase::Opening:	candidates = GetCenterCandidates();		break;
		case Phase::Middle:		candidates = GetFlipCandidates(false);	break;
		default:				candidates = GetFlipCandidates(true);	break;
		}
		break;

	case 3:
		candidates = GetOpennessCandidates();
		break;

	default:
		{
			// 想定外のレベルは左上から探して最初に置ける場所
			const std::vector<CPoint> movable = m_board.GetMovablePositions(m_color);
			if (movable.empty())
			{
				return false;
			}
			result = movable.front();
			return true;
		}
	}

	return ChooseFrom(candidates, result) && CBoard::IsInside(result);
}

CComPlayer::Candidates CComPlayer::GetCenterCandidates() const
{
	const int center = kBoardSize / 2;
	Candidates candidates;

	for (int y : kCenterFirstOrder)
	{
		for (int x : kCenterFirstOrder)
		{
			const CPoint pos(x, y);
			if (m_board.CanPut(pos, m_color))
			{
				candidates.push_back({ pos, abs(center - x) + abs(center - y) });
			}
		}
	}

	SortCandidates(candidates, true);
	return candidates;
}

CComPlayer::Candidates CComPlayer::GetFlipCandidates(bool many) const
{
	Candidates candidates;

	for (const CPoint& pos : m_board.GetMovablePositions(m_color))
	{
		FlipCounts flips;
		m_board.GetFlips(pos, m_color, flips);

		int total = 0;
		for (int count : flips)
		{
			total += count;
		}
		candidates.push_back({ pos, total });
	}

	SortCandidates(candidates, !many);
	return candidates;
}

// 開放度 = 置いたマスと裏返る石それぞれの周囲にある空きマスの合計
// 小さいほど相手に打たれる場所を増やさない良い手とみなす
CComPlayer::Candidates CComPlayer::GetOpennessCandidates() const
{
	Candidates candidates;

	for (const CPoint& pos : m_board.GetMovablePositions(m_color))
	{
		FlipCounts flips;
		m_board.GetFlips(pos, m_color, flips);

		CBoard after = m_board;
		after.Put(pos, flips, m_color);

		int openness = CountEmptyAround(pos);
		for (int y = 1; y <= kBoardSize; y++)
		{
			for (int x = 1; x <= kBoardSize; x++)
			{
				// 打つ前後で色が変わった石＝裏返る石
				const CPoint cell(x, y);
				if (cell != pos && m_board.GetAt(cell) != after.GetAt(cell))
				{
					openness += CountEmptyAround(cell);
				}
			}
		}
		candidates.push_back({ pos, openness });
	}

	SortCandidates(candidates, true);
	return candidates;
}

// 打つ前の盤面で数える
int CComPlayer::CountEmptyAround(CPoint pos) const
{
	int count = 0;

	for (int dy = -1; dy <= 1; dy++)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			const CPoint around(pos.x + dx, pos.y + dy);
			if ((dx != 0 || dy != 0) && CBoard::IsInside(around) && m_board.GetAt(around) == Stone::None)
			{
				count++;
			}
		}
	}

	return count;
}

// 序盤はランダム。それ以降は候補を評価順に見て、
// 角 > 無難な手 > 星 > 角の隣 > 相手に角を与える手 の優先度で、各分類の最初の候補を選ぶ
bool CComPlayer::ChooseFrom(const Candidates& candidates, CPoint& result)
{
	if (candidates.empty())
	{
		return false;
	}

	if (m_moveCount <= kRandomMoveCount)
	{
		std::uniform_int_distribution<size_t> dist(0, candidates.size() - 1);
		result = candidates[dist(m_random)].pos;
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

bool CComPlayer::GivesCornerToEnemy(CPoint pos) const
{
	FlipCounts flips;
	if (!m_board.GetFlips(pos, m_color, flips))
	{
		return false;
	}

	CBoard after = m_board;
	after.Put(pos, flips, m_color);

	const Stone enemy = Opponent(m_color);
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
void CComPlayer::SortCandidates(Candidates& candidates, bool ascending)
{
	for (size_t i = 0; i < candidates.size(); i++)
	{
		for (size_t j = i + 1; j < candidates.size(); j++)
		{
			const bool needSwap = ascending
				? (candidates[i].score > candidates[j].score)
				: (candidates[i].score < candidates[j].score);
			if (needSwap)
			{
				std::swap(candidates[i], candidates[j]);
			}
		}
	}
}

bool CComPlayer::IsCorner(CPoint pos)
{
	const bool edgeX = (pos.x == 1 || pos.x == kBoardSize);
	const bool edgeY = (pos.y == 1 || pos.y == kBoardSize);
	return edgeX && edgeY;
}

bool CComPlayer::IsXSquare(CPoint pos)
{
	const bool nextX = (pos.x == 2 || pos.x == kBoardSize - 1);
	const bool nextY = (pos.y == 2 || pos.y == kBoardSize - 1);
	return nextX && nextY;
}

bool CComPlayer::IsCSquare(CPoint pos)
{
	const bool edgeX = (pos.x == 1 || pos.x == kBoardSize);
	const bool edgeY = (pos.y == 1 || pos.y == kBoardSize);
	const bool nextX = (pos.x == 2 || pos.x == kBoardSize - 1);
	const bool nextY = (pos.y == 2 || pos.y == kBoardSize - 1);
	return (edgeX && nextY) || (nextX && edgeY);
}
