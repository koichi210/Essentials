// save each setting value class
typedef struct tagKihuTable{
	CPoint	zahyo ;						// put matrix
	short color ;						// put color black or white
	int put_stone[ORIENT_MAX] ;			// turn orient
	char Table[LINE_MAX][LINE_MAX] ;	// table
}StrKihuTable;



class CManager : public COthelloBase
{
//private :
public:
	StrKihuTable	KihuTable[TURN_MAX+1] ;

public :
	int play_mode ;			// PLAYER or COM
	int com_level ;			// COM level
	int com_interval ;		// COM timer
	int turning_point ;		// memory move counter of modern for reverse KIHU
	int game_state ;		// game state
	int put_notice ;		// put location notice

	CManager() ;			// construct
	virtual ~CManager() ;	// destruct

	void GameValueInit() ;										// init

	void UpDateGameState(int state) ;							// update game state
	void UpDateKihu(CPoint zahyo, int put_stone[ORIENT_MAX]) ;	// save KIHU 2 DB

	void GetKihu(int attrib, CString path) ;						// reference KIHU
	int  GetKihuFile(CString path, int * color) ;				// read KIHU
	int  GetTurnNow(int play_mode, int color) ;					// COM or PLAYER return
	void GetColorName(int clr, char name[STR_BUFF]) ;			// get color name
	void GetEnemyColor(int my_clr, int * enmy_clr) ;
};
