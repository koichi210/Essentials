// game_manager.cc : 対局の進行・設定・棋譜の管理

#include "StdAfx.h"
#include "game_manager.h"

namespace
{
	const LPCTSTR kKifuHeader = _T("***** 棋譜 ***** \n");
}

GameManager::GameManager()
	: game_state_(GameState::kInit)
	, play_mode_(PlayMode::kPlayerVsCom)
	, com_level_(3)
	, show_movable_(true)
{
	NewGame();
}

void GameManager::NewGame()
{
	board_.Reset();
	turn_ = Stone::kBlack;
	move_count_ = 0;
	record_count_ = 0;
	kifu_ = {};
	game_state_ = GameState::kInit;
}

bool GameManager::IsComTurn() const
{
	switch (play_mode_)
	{
	case PlayMode::kPlayerVsCom:	return turn_ == Stone::kWhite;
	case PlayMode::kComVsPlayer:	return turn_ == Stone::kBlack;
	case PlayMode::kComVsCom:	return true;
	default:					return false;
	}
}

bool GameManager::PlayMove(CPoint pos)
{
	FlipCounts flips;
	if (!board_.GetFlips(pos, turn_, flips))
	{
		return false;
	}

	board_.Put(pos, flips, turn_);

	// 新しい手を打ったら、一手戻していた先の棋譜は無効になる
	kifu_[move_count_] = { pos, turn_, flips };
	move_count_++;
	record_count_ = move_count_;
	return true;
}

void GameManager::Undo()
{
	if (!CanUndo())
	{
		return;
	}

	move_count_--;
	const KifuRecord& record = kifu_[move_count_];
	board_.Undo(record.pos, record.flips, record.color);
	turn_ = record.color;
}

void GameManager::Redo()
{
	if (!CanRedo())
	{
		return;
	}

	const KifuRecord& record = kifu_[move_count_];
	move_count_++;
	board_.Put(record.pos, record.flips, record.color);
	turn_ = Opponent(record.color);
}

// 書式: " 1 : F.5 黒"（手番号 : 列.行 色名）
CString GameManager::GetKifuText() const
{
	CString text = kKifuHeader;

	for (int i = 0; i < record_count_; i++)
	{
		const KifuRecord& record = kifu_[i];
		CString line;
		line.Format(_T("%2d : %c.%d %s\n"), i + 1, ColumnChar(record.pos.x), record.pos.y, ColorName(record.color));
		text += line;
	}

	return text;
}

bool GameManager::SaveKifu(LPCTSTR path) const
{
	CStdioFile file;
	if (!file.Open(path, CFile::modeCreate | CFile::modeWrite | CFile::typeText))
	{
		return false;
	}

	file.WriteString(GetKifuText());
	return true;
}

// 書式に合わない行（見出しなど）は読み飛ばし、打てない手が出てきたら失敗とする
bool GameManager::LoadKifu(LPCTSTR path)
{
	CStdioFile file;
	if (!file.Open(path, CFile::modeRead | CFile::typeText))
	{
		return false;
	}

	CString line;
	while (file.ReadString(line))
	{
		int number = 0;
		TCHAR column = 0;
		TCHAR row = 0;
		TCHAR name[16] = {};
		if (_stscanf_s(line, _T("%d : %c.%c %15s"), &number, &column, 1, &row, 1, name, static_cast<unsigned>(_countof(name))) != 4)
		{
			continue;
		}

		const CPoint pos(column - _T('A') + 1, row - _T('0'));
		if (number < 1 || kMaxMoves < number || !Board::IsInside(pos))
		{
			continue;
		}

		turn_ = (_tcscmp(name, ColorName(Stone::kBlack)) == 0) ? Stone::kBlack : Stone::kWhite;
		if (!PlayMove(pos))
		{
			return false;
		}
	}

	return true;
}
