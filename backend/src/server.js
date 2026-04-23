const path = require('path');
const dotenv = require('dotenv');

dotenv.config({ path: path.join(__dirname, '..', '.env') });

const app = require('./app');

const port = Number(process.env.PORT || 3000);

// 로컬 개발용 HTTP 서버를 띄운다.
app.listen(port, () => {
  console.log(`Backend listening on http://localhost:${port}`);
});
