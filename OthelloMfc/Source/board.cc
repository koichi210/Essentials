// board.cc : 盤面の保持と着手判定

#include "StdAfx.h"
#include "board.h"

// 左, 右, 上, 下, 左上, 左下, 右上, 右下
const CPoint CBoard::kDirections[kDirectionCount] =
{
	CPoint(-1,  0), CPoint( 1,  0), CPoint( 0, -1), CPoint( 0,  1),
	CPoint(-1, -1), CPoint(-1,  1), CPoint( 1, -1), CPoint( 1,  1),
};

CBoard::CBoard()
{
	Reset();
}

void CBoard::Reset()
{
	for (auto& row : m_cells)
	{
		for (auto& cell : row)
		{
			cell = Stone::None;
		}
	}

	const int center = kBoardSize / 2;
	At(CPoint(center,     center))     = Stone::White;
	At(CPoint(center + 1, center))     = Stone::Black;
	At(CPoint(center,     center + 1)) = Stone::Black;
	At(CPoint(center + 1, center + 1)) = Stone::White;
}

bool CBoard::IsInside(CPoint pos)
{
	return (1 <= pos.x && pos.x <= kBoardSize && 1 <= pos.y && pos.y <= kBoardSize);
}

Stone CBoard::GetAt(CPoint pos) const
{
	return m_cells[pos.y - 1][pos.x - 1];
}

Stone& CBoard::At(CPoint pos)
{
	return m_cells[pos.y - 1][pos.x - 1];
}

bool CBoard::CanPut(CPoint pos, Stone color) const
{
	FlipCounts flips;
	return GetFlips(pos, color, flips);
}

// 方向ごとに、相手の石が続いた先に自分の石があれば、その間の数だけ裏返せる
bool CBoard::GetFlips(CPoint pos, Stone color, FlipCounts& flips) const
{
	flips.fill(0);

	if (!IsInside(pos) || GetAt(pos) != Stone::None)
	{
		return false;
	}

	const Stone enemy = Opponent(color);
	bool canPut = false;

	for (int dir = 0; dir < kDirectionCount; dir++)
	{
		CPoint cur = pos + kDirections[dir];
		int count = 0;

		while (IsInside(cur) && GetAt(cur) == enemy)
		{
			count++;
			cur += kDirections[dir];
		}

		if (count > 0 && IsInside(cur) && GetAt(cur) == color)
		{
			flips[dir] = count;
			canPut = true;
		}
	}

	return canPut;
}

std::vector<CPoint> CBoard::GetMovablePositions(Stone color) const
{
	std::vector<CPoint> positions;

	for (int y = 1; y <= kBoardSize; y++)
	{
		for (int x = 1; x <= kBoardSize; x++)
		{
			if (CanPut(CPoint(x, y), color))
			{
				positions.push_back(CPoint(x, y));
			}
		}
	}

	return positions;
}

bool CBoard::HasMovablePosition(Stone color) const
{
	for (int y = 1; y <= kBoardSize; y++)
	{
		for (int x = 1; x <= kBoardSize; x++)
		{
			if (CanPut(CPoint(x, y), color))
			{
				return true;
			}
		}
	}

	return false;
}

// 相手が置ければ相手、相手が置けず自分が置ければ自分（相手はパス）、どちらも置けなければ終局
Stone CBoard::GetNextTurn(Stone mover) const
{
	const Stone enemy = Opponent(mover);

	if (HasMovablePosition(enemy))
	{
		return enemy;
	}
	if (HasMovablePosition(mover))
	{
		return mover;
	}
	return Stone::None;
}

void CBoard::Put(CPoint pos, const FlipCounts& flips, Stone color)
{
	At(pos) = color;
	Flip(pos, flips, color);
}

void CBoard::Undo(CPoint pos, const FlipCounts& flips, Stone mover)
{
	At(pos) = Stone::None;
	Flip(pos, flips, Opponent(mover));
}

void CBoard::Flip(CPoint pos, const FlipCounts& flips, Stone color)
{
	for (int dir = 0; dir < kDirectionCount; dir++)
	{
		CPoint cur = pos;
		for (int i = 0; i < flips[dir]; i++)
		{
			cur += kDirections[dir];
			At(cur) = color;
		}
	}
}

void CBoard::CountStones(int& black, int& white) const
{
	black = 0;
	white = 0;

	for (const auto& row : m_cells)
	{
		for (Stone cell : row)
		{
			if (cell == Stone::Black)
			{
				black++;
			}
			else if (cell == Stone::White)
			{
				white++;
			}
		}
	}
}
