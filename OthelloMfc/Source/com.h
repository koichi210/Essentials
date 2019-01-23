// COM think routin class

class CComProc : public COthelloBase
{
public :
	char OrgTable[LINE_MAX][LINE_MAX] ;	// original table(stack)
	int field ;							// move state of game
	CComProc() ;			// construct
	virtual ~CComProc() ;	// destrcut

	int Turn(CPoint * zahyo, int clr, int level, int turn_cnt) ;	// COM tink routin MAIN
	int DetermineLocation(CPoint put[MAS_MAX], CPoint * determ_put, int put_num) ;		// where put stone

	int ComLevel1(CPoint * zahyo) ;		// COMLevel1
	int ComLevel2(CPoint * zahyo) ;		// COMLevel2
	int ComLevel3(CPoint * zahyo) ;		// COMLevel3

	int	Corner(CPoint zahyo) ;			// is corner?
	int	CornerPP(int color) ;			// judge pingpoint corener?
	int	Star(CPoint zahyo) ;			// is star?
	int	StarNear(CPoint zahyo) ;		// is next star?
	int AllSteal(CPoint zahyo, int flg) ;		// is stell all?
	int	LeftUp(CPoint * zahyo) ;		// move to left up
	int	Straight(CPoint * zahyo) ;		// straight
	int StealCorner(CPoint zahyo) ;		// is get corner?

	int	Center(CPoint put[MAS_MAX], int * put_num) ;		// move to center
	int	GetMax(CPoint put[MAS_MAX], int * put_num) ;		// a lot of overturn
	int	GetMin(CPoint put[MAS_MAX], int * put_num) ;		// a little overturn

	int	GetKaiho(CPoint put[MAS_MAX], int * put_num) ;		// open theory
	int	Kaiho(CPoint zahyo, int put_stone[ORIENT_MAX]) ;	// open rate
	int	KaihoSub(int x, int y) ;							// calculation to open rate
};
