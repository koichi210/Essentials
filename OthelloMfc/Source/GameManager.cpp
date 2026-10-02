// GameManager.cpp : 対局の進行・設定・棋譜の管理

#include "StdAfx.h"
#include "GameManager.h"

namespace
{
	const LPCTSTR kKifuHeader = _T("***** 棋譜 ***** \n");

	// 棋譜の列表記（1→'A'）
	TCHAR ToColumnChar(int x)
	{
		return static_cast<TCHAR>(_T('A') + x - 1);
	}
}

CGameManager::CGameManager()
	: m_gameState(GameState::Init)
	, m_playMode(PlayMode::PlayerVsCom)
	, m_comLevel(3)
	, m_showMovable(true)
{
	NewGame();
}

void CGameManager::NewGame()
{
	m_board.Reset();
	m_turn = Stone::Black;
	m_moveCount = 0;
	m_recordCount = 0;
	m_kifu = {};
	m_gameState = GameState::Init;
}

bool CGameManager::IsComTurn() const
{
	switch (m_playMode)
	{
	case PlayMode::PlayerVsCom:	return m_turn == Stone::White;
	case PlayMode::ComVsPlayer:	return m_turn == Stone::Black;
	case PlayMode::ComVsCom:	return true;
	default:					return false;
	}
}

bool CGameManager::PlayMove(CPoint pos)
{
	FlipCounts flips;
	if (!m_board.GetFlips(pos, m_turn, flips))
	{
		return false;
	}

	m_board.Put(pos, flips, m_turn);

	// 新しい手を打ったら、一手戻していた先の棋譜は無効になる
	m_kifu[m_moveCount] = { pos, m_turn, flips };
	m_moveCount++;
	m_recordCount = m_moveCount;
	return true;
}

void CGameManager::Undo()
{
	if (!CanUndo())
	{
		return;
	}

	m_moveCount--;
	const KifuRecord& record = m_kifu[m_moveCount];
	m_board.Undo(record.pos, record.flips, record.color);
	m_turn = record.color;
}

void CGameManager::Redo()
{
	if (!CanRedo())
	{
		return;
	}

	const KifuRecord& record = m_kifu[m_moveCount];
	m_moveCount++;
	m_board.Put(record.pos, record.flips, record.color);
	m_turn = Opponent(record.color);
}

// 書式: " 1 : F.5 黒"（手番号 : 列.行 色名）
CString CGameManager::GetKifuText() const
{
	CString text = kKifuHeader;

	for (int i = 0; i < m_recordCount; i++)
	{
		const KifuRecord& record = m_kifu[i];
		CString line;
		line.Format(_T("%2d : %c.%d %s\n"), i + 1, ToColumnChar(record.pos.x), record.pos.y, ColorName(record.color));
		text += line;
	}

	return text;
}

bool CGameManager::SaveKifu(LPCTSTR path) const
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
bool CGameManager::LoadKifu(LPCTSTR path)
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
		if (number < 1 || kMaxMoves < number || !CBoard::IsInside(pos))
		{
			continue;
		}

		m_turn = (_tcscmp(name, ColorName(Stone::Black)) == 0) ? Stone::Black : Stone::White;
		if (!PlayMove(pos))
		{
			return false;
		}
	}

	return true;
}
