pub fn add(left: usize, right: usize) -> usize {
    left + right
}

pub fn fibonacci(n: u32) -> u32 {
    n
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{thread, time};
    use rstest::*;
    use test_case::test_case;

    #[test]
    fn it_works() {
        let val = std::env::var("ENV_VAR_1").unwrap_or("NOT FOUND!".to_string());
        assert_eq!(val, "ENV_VAR_1_VALUE");
    }

    #[test]
    #[ignore = "ignored for now!"]
    fn it_works245_ignored() {
        assert_eq!(0, 0);
    }

    #[rstest]
    #[case(0, 0)]
    #[case(1, 1)]
    #[case(2, 1)]
    fn fibonacci_test(#[case] input: u32, #[case] expected: u32) {
        assert_eq!(expected, fibonacci(input))
    }

    #[fixture]
    pub fn fixture() -> u32 {
        42
    }

    #[rstest]
    fn should_success(fixture: u32) {
        assert_eq!(fixture, 42);
    }

    #[rstest]
    fn should_fail(fixture: u32) {
        assert_ne!(fixture, 42);
    }

    #[test_case(-2, -4 ; "when both operands are negative")]
    #[test_case(2,  4  ; "when both operands are positive")]
    #[test_case(4,  2  ; "when operands are swapped")]
    fn multiplication_tests(x: i8, y: i8) {
        let actual = (x * y).abs();
        assert_eq!(8, actual)
    }

    #[test]
    #[should_panic(expected = "values don't match")]
    fn mytest() {
        assert_eq!(1, 2, "values don't match");
    }

    #[test]
    fn long_running_test() {
        thread::sleep(time::Duration::from_millis(1000));
        assert_eq!(1, 1);
    }
}
