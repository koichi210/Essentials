// board.cc : 盤面の保持と着手判定

#include "StdAfx.h"
#include "board.h"

// 左, 右, 上, 下, 左上, 左下, 右上, 右下
const CPoint Board::kDirections[kDirectionCount] =
{
	CPoint(-1,  0), CPoint( 1,  0), CPoint( 0, -1), CPoint( 0,  1),
	CPoint(-1, -1), CPoint(-1,  1), CPoint( 1, -1), CPoint( 1,  1),
};

Board::Board()
{
	Reset();
}

void Board::Reset()
{
	for (auto& row : cells_)
	{
		for (auto& cell : row)
		{
			cell = Stone::kNone;
		}
	}

	const int center = kBoardSize / 2;
	At(CPoint(center,     center))     = Stone::kWhite;
	At(CPoint(center + 1, center))     = Stone::kBlack;
	At(CPoint(center,     center + 1)) = Stone::kBlack;
	At(CPoint(center + 1, center + 1)) = Stone::kWhite;
}

bool Board::IsInside(CPoint pos)
{
	return (1 <= pos.x && pos.x <= kBoardSize && 1 <= pos.y && pos.y <= kBoardSize);
}

Stone Board::GetAt(CPoint pos) const
{
	return cells_[pos.y - 1][pos.x - 1];
}

Stone& Board::At(CPoint pos)
{
	return cells_[pos.y - 1][pos.x - 1];
}

// 相手の石が続いた先に自分の石があれば、その間の数だけ裏返せる
int Board::CountFlipsInDirection(CPoint pos, int dir, Stone color) const
{
	const Stone enemy = Opponent(color);
	CPoint cur = pos + kDirections[dir];
	int count = 0;

	while (IsInside(cur) && GetAt(cur) == enemy)
	{
		count++;
		cur += kDirections[dir];
	}

	return (count > 0 && IsInside(cur) && GetAt(cur) == color) ? count : 0;
}

// 置けるかどうかだけ知りたいときは、裏返る方向が1つ見つかった時点で打ち切る
bool Board::CanPut(CPoint pos, Stone color) const
{
	if (!IsInside(pos) || GetAt(pos) != Stone::kNone)
	{
		return false;
	}

	for (int dir = 0; dir < kDirectionCount; dir++)
	{
		if (CountFlipsInDirection(pos, dir, color) > 0)
		{
			return true;
		}
	}
	return false;
}

bool Board::GetFlips(CPoint pos, Stone color, FlipCounts& flips) const
{
	flips.fill(0);

	if (!IsInside(pos) || GetAt(pos) != Stone::kNone)
	{
		return false;
	}

	bool can_put = false;
	for (int dir = 0; dir < kDirectionCount; dir++)
	{
		flips[dir] = CountFlipsInDirection(pos, dir, color);
		if (flips[dir] > 0)
		{
			can_put = true;
		}
	}

	return can_put;
}

std::vector<CPoint> Board::GetMovablePositions(Stone color) const
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

bool Board::HasMovablePosition(Stone color) const
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
Stone Board::GetNextTurn(Stone mover) const
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
	return Stone::kNone;
}

void Board::Put(CPoint pos, const FlipCounts& flips, Stone color)
{
	At(pos) = color;
	Flip(pos, flips, color);
}

void Board::Undo(CPoint pos, const FlipCounts& flips, Stone mover)
{
	At(pos) = Stone::kNone;
	Flip(pos, flips, Opponent(mover));
}

void Board::Flip(CPoint pos, const FlipCounts& flips, Stone color)
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

void Board::CountStones(int& black, int& white) const
{
	black = 0;
	white = 0;

	for (const auto& row : cells_)
	{
		for (Stone cell : row)
		{
			if (cell == Stone::kBlack)
			{
				black++;
			}
			else if (cell == Stone::kWhite)
			{
				white++;
			}
		}
	}
}
