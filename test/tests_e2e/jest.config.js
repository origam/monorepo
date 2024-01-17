module.exports = {
  //preset: "jest-puppeteer",
  testMatch: ["./**/*.test.js"],
  verbose: true,
  setupFilesAfterEnv: ["./jest.setup.js"],
  reporters: [
    "default",
    [
      "jest-trx-results-processor",
      {
        outputFile: "frontend-integration-test-results.trx"
      }
    ]
  ]
};


