// othello base and put judge class

class COthelloBase
{
public : 
	char Table[LINE_MAX][LINE_MAX] ;	// table
	int color ;			// modern turn (white or black)
	int turn_cnt ;		// move counter

	COthelloBase() ;			// construct
	virtual ~COthelloBase() ;	// destruct

	void BaseInit() ;			// init

	void GetTable(char m_Table[LINE_MAX][LINE_MAX]) ;		// get manager data
	void GetPutNotice(int clr, CPoint zahyo[LINE_MAX*LINE_MAX]) ;	// notice put stone

	void CntStone(int * black_num, int * white_num) ;	// calculation
	void PutStone(CPoint zahyo, int put_stone[ORIENT_MAX], int color, int attrib = VERS) ;	// put stone

	int  PutCheckWhich(int color) ;	// which black or white put matrix?
	int  PutCheckAny(int color) ;	// where put matrix?
	int  PutCheck(CPoint zahyo, int color, int attrib, int put_stone[ORIENT_MAX]) ;	// judge put stone
};
