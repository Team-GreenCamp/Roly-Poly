const path = require('path');
const dotenv = require('dotenv');

dotenv.config({ path: path.join(__dirname, '..', '.env') });

const app = require('./app');

const port = Number(process.env.PORT || 3000);

// 놓친 async 오류가 있어도 프로세스 전체가 내려가지 않도록 기록만 한다.
process.on('unhandledRejection', (reason) => {
  console.error('Unhandled rejection:', reason);
});

// 로컬 개발용 HTTP 서버를 띄운다.
app.listen(port, () => {
  console.log(`Backend listening on http://localhost:${port}`);
});
